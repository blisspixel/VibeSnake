"""Build one deterministic, human-approved optional radio pack."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import stat
import sys
from io import BytesIO
from pathlib import Path
from typing import Any, Mapping
from zipfile import ZIP_STORED, ZipFile, ZipInfo

try:
    from _checkout import promote_checkout_source
except ModuleNotFoundError:  # Imported as scripts.assemble_radio_pack in tests.
    from scripts._checkout import promote_checkout_source


ROOT = Path(__file__).resolve().parents[1]
SRC = promote_checkout_source(ROOT)

from vibesnake.content.inventory import ContentInventoryError, check_inventory  # noqa: E402
from vibesnake.content.packs import (  # noqa: E402
    CONTENT_PACK_MANIFEST_MAX_BYTES,
    ContentPackError,
    check_pack_manifest,
)


CURATION_STATUS = "approved-for-alpha-release"
PACK_EXTENSION = ".vibesnake-pack.zip"
MANIFEST_NAME = "pack.json"
ASSEMBLY_NAME = "radio_pack_assembly.json"
CHECKSUM_NAME = "SHA256SUMS.txt"
MAXIMUM_COMPRESSED_BYTES = 80 * 1024 * 1024
MAXIMUM_INSTALLED_BYTES = 120 * 1024 * 1024
MAXIMUM_CURATION_BYTES = 1_048_576
_STATION_ID_PATTERN = re.compile(r"[a-z0-9]+(?:_[a-z0-9]+)*\Z")
_CURATION_FIELDS = {
    "schemaVersion",
    "planId",
    "inventoryPolicySha256",
    "decisionStatus",
    "coreMusic",
    "stations",
}
_DECISION_FIELDS = {"pendingAssetIds", "approvedAssetIds", "rejectedAssetIds"}
_STATION_FIELDS = {"id", *_DECISION_FIELDS}


class RadioPackAssemblyError(ValueError):
    """Raised when reviewed radio content cannot be packaged safely."""


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _reject_duplicate_fields(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise RadioPackAssemblyError(f"content curation repeats JSON field: {key}")
        result[key] = value
    return result


def _load_curation(path: Path) -> dict[str, Any]:
    try:
        if path.stat().st_size > MAXIMUM_CURATION_BYTES:
            raise RadioPackAssemblyError(f"content curation exceeds the {MAXIMUM_CURATION_BYTES}-byte limit")
        source = path.read_text(encoding="utf-8")
        if len(source.encode("utf-8")) > MAXIMUM_CURATION_BYTES:
            raise RadioPackAssemblyError(f"content curation exceeds the {MAXIMUM_CURATION_BYTES}-byte limit")
        value = json.loads(source, object_pairs_hook=_reject_duplicate_fields)
    except RadioPackAssemblyError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RadioPackAssemblyError(f"content curation is unreadable: {path}: {error}") from error
    if not isinstance(value, dict) or set(value) != _CURATION_FIELDS:
        raise RadioPackAssemblyError("content curation must use the exact schema 1 fields")
    if value["schemaVersion"] != 1 or value["planId"] != "vibesnake-content-curation-v1":
        raise RadioPackAssemblyError("content curation identity is unsupported")
    if value["decisionStatus"] != CURATION_STATUS:
        raise RadioPackAssemblyError(f"content curation must be {CURATION_STATUS}")
    if not isinstance(value["stations"], list) or not value["stations"]:
        raise RadioPackAssemblyError("content curation stations must be a nonempty array")
    return value


def _validated_decisions(value: Any, label: str, *, station: bool = False) -> dict[str, Any]:
    expected = _STATION_FIELDS if station else _DECISION_FIELDS
    if not isinstance(value, dict) or set(value) != expected:
        raise RadioPackAssemblyError(f"{label} must use the exact decision fields")
    decisions: list[set[str]] = []
    for field in sorted(_DECISION_FIELDS):
        items = value[field]
        if (
            not isinstance(items, list)
            or any(not isinstance(item, str) or not item for item in items)
            or len(items) != len(set(items))
        ):
            raise RadioPackAssemblyError(f"{label}.{field} must contain unique nonempty asset IDs")
        decisions.append(set(items))
    if any(left & right for index, left in enumerate(decisions) for right in decisions[index + 1 :]):
        raise RadioPackAssemblyError(f"{label} decisions must be disjoint")
    if station and (not isinstance(value["id"], str) or not value["id"]):
        raise RadioPackAssemblyError(f"{label}.id must be a nonempty station ID")
    return value


def _approved_station(
    curation: Mapping[str, Any],
    station_id: str,
    inventory: Mapping[str, Any],
) -> dict[str, Any]:
    core = _validated_decisions(curation["coreMusic"], "content curation coreMusic")
    assets = inventory.get("assets")
    if not isinstance(assets, list):
        raise RadioPackAssemblyError("content inventory assets must be an array")
    inventory_ids = {
        entry.get("id") for entry in assets if isinstance(entry, Mapping) and isinstance(entry.get("id"), str)
    }
    radio_ids = {
        entry["id"]
        for entry in assets
        if isinstance(entry, Mapping)
        and isinstance(entry.get("id"), str)
        and entry.get("mediaType") == "audio/mpeg"
        and isinstance(entry.get("path"), str)
        and entry["path"].startswith("audio/radio/")
    }
    core_ids = set().union(*(set(core[field]) for field in _DECISION_FIELDS))
    if not core_ids <= inventory_ids or core_ids & radio_ids:
        raise RadioPackAssemblyError("content curation coreMusic contains unknown or radio asset IDs")
    stations: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    accounted_radio_ids: set[str] = set()
    for index, raw_station in enumerate(curation["stations"]):
        station = _validated_decisions(raw_station, f"content curation station {index}", station=True)
        if not _STATION_ID_PATTERN.fullmatch(station["id"]) or len(station["id"]) > 128:
            raise RadioPackAssemblyError(f"content curation station {index}.id is invalid")
        if station["id"] in seen_ids:
            raise RadioPackAssemblyError(f"content curation repeats station ID: {station['id']}")
        seen_ids.add(station["id"])
        decisions = set().union(*(set(station[field]) for field in _DECISION_FIELDS))
        if not decisions <= radio_ids:
            raise RadioPackAssemblyError(
                f"content curation station {station['id']} contains unknown or non-radio asset IDs"
            )
        if accounted_radio_ids & decisions:
            raise RadioPackAssemblyError("content curation assigns a radio asset to multiple stations")
        accounted_radio_ids.update(decisions)
        if station["id"] == station_id:
            stations.append(station)
    if accounted_radio_ids != radio_ids:
        raise RadioPackAssemblyError("content curation must account for every inventoried radio asset exactly once")
    if len(stations) != 1:
        raise RadioPackAssemblyError(f"content curation must contain exactly one station {station_id}")
    station = stations[0]
    if station["pendingAssetIds"]:
        raise RadioPackAssemblyError(f"station {station_id} still has pending listening decisions")
    if not station["approvedAssetIds"]:
        raise RadioPackAssemblyError(f"station {station_id} has no approved radio tracks")
    return station


def _zip_entry(name: str, value: bytes) -> tuple[ZipInfo, bytes]:
    entry = ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    entry.compress_type = ZIP_STORED
    entry.create_system = 3
    entry.external_attr = (stat.S_IFREG | 0o644) << 16
    return entry, value


def _safe_asset_path(asset_root: Path, relative_path: str) -> Path:
    source = asset_root.joinpath(*relative_path.split("/"))
    try:
        source.resolve(strict=True).relative_to(asset_root.resolve(strict=True))
    except (OSError, ValueError) as error:
        raise RadioPackAssemblyError(f"pack asset is missing or escapes the asset root: {relative_path}") from error
    current = source
    while current != asset_root:
        if current.is_symlink():
            raise RadioPackAssemblyError(f"pack asset path cannot contain a symbolic link: {relative_path}")
        current = current.parent
    if not source.is_file():
        raise RadioPackAssemblyError(f"pack asset is not a regular file: {relative_path}")
    return source


def _render_archive(repository_root: Path, manifest_path: Path, manifest: Mapping[str, Any]) -> bytes:
    asset_root = repository_root / str(manifest["inventory"]["assetRoot"])
    entries: list[tuple[ZipInfo, bytes]] = [_zip_entry(MANIFEST_NAME, manifest_path.read_bytes())]
    for file_entry in sorted(manifest["files"], key=lambda item: item["path"]):
        relative_path = str(file_entry["path"])
        source = _safe_asset_path(asset_root, relative_path)
        value = source.read_bytes()
        if len(value) != file_entry["bytes"] or _sha256_bytes(value) != file_entry["sha256"]:
            raise RadioPackAssemblyError(f"pack asset changed after inventory validation: {relative_path}")
        entries.append(_zip_entry(relative_path, value))
    output = BytesIO()
    with ZipFile(output, "w", compression=ZIP_STORED, allowZip64=True) as archive:
        for entry, value in entries:
            archive.writestr(entry, value)
    return output.getvalue()


def assemble_radio_pack(
    repository_root: Path,
    manifest_path: Path,
    curation_path: Path,
    inventory_path: Path,
    output_root: Path,
) -> dict[str, Any]:
    """Validate reviewed source and create one deterministic radio-pack artifact."""
    repository_root = repository_root.resolve()
    manifest_path = manifest_path.resolve()
    curation_path = curation_path.resolve()
    inventory_path = inventory_path.resolve()
    output_root = output_root.resolve()
    if output_root.exists():
        raise RadioPackAssemblyError(f"radio pack output must not already exist: {output_root}")

    try:
        if manifest_path.stat().st_size > CONTENT_PACK_MANIFEST_MAX_BYTES:
            raise RadioPackAssemblyError(f"content pack exceeds the {CONTENT_PACK_MANIFEST_MAX_BYTES}-byte limit")
        inventory = check_inventory(repository_root, inventory_path=inventory_path)
        manifest = check_pack_manifest(manifest_path, inventory)
    except RadioPackAssemblyError:
        raise
    except (ContentInventoryError, ContentPackError, OSError, UnicodeError) as error:
        raise RadioPackAssemblyError(str(error)) from error
    if manifest["kind"] != "radio" or not isinstance(manifest["radio"], Mapping):
        raise RadioPackAssemblyError("release radio-pack assembly requires one radio manifest")
    curation = _load_curation(curation_path)
    if curation["inventoryPolicySha256"] != inventory["policySha256"]:
        raise RadioPackAssemblyError("content curation policy hash does not match the inventory")
    station_id = str(manifest["radio"]["stationId"])
    station = _approved_station(curation, station_id, inventory)
    track_ids = list(manifest["radio"]["trackIds"])
    if set(track_ids) != set(station["approvedAssetIds"]):
        raise RadioPackAssemblyError("radio manifest trackIds must equal the station's approved listening decisions")
    radio_file_ids = {
        entry["id"]
        for entry in manifest["files"]
        if entry["role"] == "radio-track" and entry["mediaType"] == "audio/mpeg"
    }
    if set(track_ids) != radio_file_ids:
        raise RadioPackAssemblyError("radio manifest trackIds must list every packaged radio-track file")
    installed_bytes = sum(int(entry["bytes"]) for entry in manifest["files"])
    if installed_bytes > MAXIMUM_INSTALLED_BYTES:
        raise RadioPackAssemblyError("radio pack exceeds the installed-size budget")

    output_created = False
    try:
        archive_bytes = _render_archive(repository_root, manifest_path, manifest)
        if len(archive_bytes) > MAXIMUM_COMPRESSED_BYTES:
            raise RadioPackAssemblyError("radio pack exceeds the compressed-size budget")
        file_name = f"{manifest['id']}-{manifest['version']}{PACK_EXTENSION}"
        evidence: dict[str, Any] = {
            "schemaVersion": 1,
            "kind": "approved-radio-pack-assembly-v1",
            "passed": True,
            "releaseApproved": True,
            "packId": manifest["id"],
            "packVersion": manifest["version"],
            "stationId": station_id,
            "stationName": manifest["radio"]["stationName"],
            "curationDecisionStatus": curation["decisionStatus"],
            "inventorySha256": _sha256(inventory_path),
            "curationSha256": _sha256(curation_path),
            "manifestSha256": _sha256(manifest_path),
            "packFileName": file_name,
            "packBytes": len(archive_bytes),
            "packSha256": _sha256_bytes(archive_bytes),
            "trackCount": len(track_ids),
            "trackIds": track_ids,
        }
        output_root.mkdir(parents=True)
        output_created = True
        (output_root / file_name).write_bytes(archive_bytes)
        shutil.copyfile(manifest_path, output_root / MANIFEST_NAME)
        evidence_path = output_root / ASSEMBLY_NAME
        evidence_path.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        checksum_rows = [
            (_sha256(output_root / file_name), file_name),
            (_sha256(output_root / MANIFEST_NAME), MANIFEST_NAME),
            (_sha256(evidence_path), ASSEMBLY_NAME),
        ]
        (output_root / CHECKSUM_NAME).write_text(
            "".join(f"{digest}  {name}\n" for digest, name in sorted(checksum_rows, key=lambda row: row[1])),
            encoding="utf-8",
        )
        return evidence
    except RadioPackAssemblyError:
        if output_created:
            shutil.rmtree(output_root, ignore_errors=True)
        raise
    except (OSError, UnicodeError) as error:
        if output_created:
            shutil.rmtree(output_root, ignore_errors=True)
        raise RadioPackAssemblyError(f"could not assemble radio pack: {error}") from error


def main(argv: list[str] | None = None) -> int:
    """Build one approved radio pack or report a bounded validation error."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--curation", type=Path, default=ROOT / "config" / "content_curation_v1.json")
    parser.add_argument("--inventory", type=Path, default=ROOT / "config" / "content_inventory.json")
    parser.add_argument("--repository-root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        evidence = assemble_radio_pack(
            args.repository_root,
            args.manifest,
            args.curation,
            args.inventory,
            args.output,
        )
    except RadioPackAssemblyError as error:
        print(f"Radio pack assembly failed: {error}", file=sys.stderr)
        return 1
    print(
        f"Approved radio pack assembled: {evidence['packFileName']} "
        f"tracks={evidence['trackCount']} sha256={evidence['packSha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
