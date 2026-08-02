"""Strict manifests for the offline core pack and optional radio packs."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
import json
from pathlib import Path, PurePosixPath
import re
from typing import Any, Mapping, Sequence

from vibesnake.content.inventory import CONTENT_INVENTORY_SCHEMA_VERSION
from vibesnake.core.ruleset import CURRENT_RULESET


CONTENT_PACK_SCHEMA_VERSION = 1
CORE_PACK_ID = "vibesnake.core"
CURRENT_RULESET_ID = CURRENT_RULESET.id
CURRENT_RULESET_VERSION = CURRENT_RULESET.version

_PACK_KINDS = {"core", "radio"}
_RUNTIME_USES = {"required", "optional"}
_PACK_FIELDS = {
    "schemaVersion",
    "id",
    "version",
    "kind",
    "displayName",
    "description",
    "compatibility",
    "inventory",
    "dependencies",
    "files",
    "credits",
    "radio",
}
_COMPATIBILITY_FIELDS = {"gameVersion", "ruleset"}
_VERSION_RANGE_FIELDS = {"minInclusive", "maxExclusive"}
_RULESET_RANGE_FIELDS = {"id", "minInclusive", "maxExclusive"}
_INVENTORY_BINDING_FIELDS = {"schemaVersion", "assetRoot", "policySha256"}
_DEPENDENCY_FIELDS = {"id", "minInclusive", "maxExclusive"}
_FILE_FIELDS = {
    "id",
    "path",
    "mediaType",
    "bytes",
    "sha256",
    "role",
    "runtimeUse",
    "creditId",
}
_CREDIT_FIELDS = {
    "id",
    "source",
    "license",
    "attribution",
    "reviewEvidence",
}
_RADIO_FIELDS = {"stationId", "stationName", "trackIds"}
_IDENTIFIER = re.compile(r"[a-z0-9]+(?:[.-][a-z0-9]+)*\Z")
_SEMVER = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z")
_LOWER_SHA256 = re.compile(r"[0-9a-f]{64}\Z")


class ContentPackError(ValueError):
    """Raised when a pack is unsafe, incomplete, or inconsistent with inventory."""


@dataclass(frozen=True)
class PackCompatibilityResult:
    """One actionable compatibility decision for a structurally valid pack."""

    compatible: bool
    code: str
    message: str


@dataclass(frozen=True)
class PackSetResolution:
    """Core readiness plus independently accepted or rejected optional packs."""

    core: PackCompatibilityResult
    accepted_optional: tuple[str, ...]
    rejected_optional: Mapping[str, PackCompatibilityResult]

    @property
    def core_ready(self) -> bool:
        """Return whether the offline core can launch."""
        return self.core.compatible


def validate_pack_manifest(
    document: Mapping[str, Any],
    inventory: Mapping[str, Any],
) -> dict[str, Any]:
    """Return a detached manifest after strict schema and inventory validation."""
    manifest = deepcopy(_require_object(document, "content pack"))
    _require_exact_fields(manifest, _PACK_FIELDS, "content pack")
    if type(manifest["schemaVersion"]) is not int or manifest["schemaVersion"] != CONTENT_PACK_SCHEMA_VERSION:
        raise ContentPackError(f"unsupported content pack schema: {manifest['schemaVersion']}")

    pack_id = _require_identifier(manifest["id"], "content pack id")
    _parse_semver(manifest["version"], "content pack version")
    kind = _require_text(manifest["kind"], "content pack kind")
    if kind not in _PACK_KINDS:
        raise ContentPackError(f"content pack has invalid kind: {kind}")
    _require_text(manifest["displayName"], "content pack displayName")
    _require_text(manifest["description"], "content pack description")

    _validate_compatibility(manifest["compatibility"])
    _validate_inventory_binding(manifest["inventory"], inventory)
    dependencies = _validate_dependencies(manifest["dependencies"], pack_id)
    credits = _validate_credits(manifest["credits"])
    files = _validate_files(manifest["files"], credits)
    _validate_radio(manifest["radio"], kind, pack_id, files)

    if kind == "core":
        if pack_id != CORE_PACK_ID:
            raise ContentPackError(f"the core pack id must be {CORE_PACK_ID}")
        if dependencies:
            raise ContentPackError("the offline core pack cannot have dependencies")
        if not any(entry["runtimeUse"] == "required" for entry in files):
            raise ContentPackError("the core pack must contain required runtime content")
    else:
        dependency_ids = {dependency["id"] for dependency in dependencies}
        if dependency_ids != {CORE_PACK_ID}:
            raise ContentPackError(f"a radio pack must depend only on {CORE_PACK_ID}")
        if any(entry["runtimeUse"] != "optional" for entry in files):
            raise ContentPackError("radio pack files must have optional runtime use")

    _validate_files_against_inventory(manifest, inventory, files, credits)
    return manifest


def load_pack_manifest(
    path: Path,
    inventory: Mapping[str, Any],
) -> dict[str, Any]:
    """Load and validate one pack manifest from disk."""
    try:
        document = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_unique_json_object,
        )
    except FileNotFoundError as error:
        raise ContentPackError(f"content pack does not exist: {path}") from error
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ContentPackError(f"content pack is unreadable: {path}: {error}") from error
    return validate_pack_manifest(document, inventory)


def render_pack_manifest(manifest: Mapping[str, Any]) -> str:
    """Render the deterministic checked-in pack representation."""
    return (
        json.dumps(
            manifest,
            ensure_ascii=False,
            indent=2,
            sort_keys=True,
        )
        + "\n"
    )


def check_pack_manifest(
    path: Path,
    inventory: Mapping[str, Any],
) -> dict[str, Any]:
    """Require a manifest to be valid and canonically encoded."""
    manifest = load_pack_manifest(path, inventory)
    if path.read_text(encoding="utf-8") != render_pack_manifest(manifest):
        raise ContentPackError(f"content pack is not canonically encoded: {path}")
    return manifest


def evaluate_pack_compatibility(
    manifest: Mapping[str, Any],
    *,
    game_version: str,
    ruleset_id: str,
    ruleset_version: int,
    installed_packs: Mapping[str, str],
) -> PackCompatibilityResult:
    """Evaluate app, rules, and dependency ranges without loading any files."""
    current_game = _parse_semver(game_version, "current game version")
    compatibility = manifest["compatibility"]
    game_range = compatibility["gameVersion"]
    minimum_game = _parse_semver(
        game_range["minInclusive"],
        "compatible minimum game version",
    )
    maximum_game = _parse_semver(
        game_range["maxExclusive"],
        "compatible maximum game version",
    )
    if current_game < minimum_game:
        return _incompatible(
            "game-version-too-old",
            f"Pack {manifest['id']} requires game {game_range['minInclusive']} or newer.",
        )
    if current_game >= maximum_game:
        return _incompatible(
            "game-version-too-new",
            f"Pack {manifest['id']} does not support game {game_version}.",
        )

    ruleset = compatibility["ruleset"]
    if ruleset_id != ruleset["id"]:
        return _incompatible(
            "ruleset-mismatch",
            f"Pack {manifest['id']} targets ruleset {ruleset['id']}.",
        )
    if ruleset_version < ruleset["minInclusive"]:
        return _incompatible(
            "rules-version-too-old",
            f"Pack {manifest['id']} requires rules version {ruleset['minInclusive']} or newer.",
        )
    if ruleset_version >= ruleset["maxExclusive"]:
        return _incompatible(
            "rules-version-too-new",
            f"Pack {manifest['id']} does not support rules version {ruleset_version}.",
        )

    for dependency in manifest["dependencies"]:
        installed = installed_packs.get(dependency["id"])
        if installed is None:
            return _incompatible(
                "missing-dependency",
                f"Pack {manifest['id']} requires {dependency['id']}.",
            )
        installed_version = _parse_semver(
            installed,
            f"installed version of {dependency['id']}",
        )
        minimum = _parse_semver(
            dependency["minInclusive"],
            f"minimum version of {dependency['id']}",
        )
        maximum = _parse_semver(
            dependency["maxExclusive"],
            f"maximum version of {dependency['id']}",
        )
        if installed_version < minimum:
            return _incompatible(
                "dependency-version-too-old",
                f"Pack {manifest['id']} requires a newer {dependency['id']}.",
            )
        if installed_version >= maximum:
            return _incompatible(
                "dependency-version-too-new",
                f"Pack {manifest['id']} does not support installed {dependency['id']}.",
            )

    return PackCompatibilityResult(
        True,
        "compatible",
        f"Pack {manifest['id']} is compatible.",
    )


def resolve_pack_set(
    core_document: Mapping[str, Any],
    optional_documents: Sequence[Mapping[str, Any]],
    inventory: Mapping[str, Any],
    *,
    game_version: str,
    ruleset_id: str = CURRENT_RULESET_ID,
    ruleset_version: int = CURRENT_RULESET_VERSION,
) -> PackSetResolution:
    """Resolve optional packs independently so one failure never blocks core play."""
    core = validate_pack_manifest(core_document, inventory)
    if core["kind"] != "core":
        raise ContentPackError("the required pack-set document is not a core pack")

    rejected: dict[str, PackCompatibilityResult] = {}
    optional: dict[str, dict[str, Any]] = {}
    claimed_optional_ids: set[str] = set()
    for index, document in enumerate(optional_documents):
        fallback_id = f"optional[{index}]"
        if isinstance(document, Mapping) and isinstance(document.get("id"), str):
            fallback_id = document["id"]
        if fallback_id in claimed_optional_ids:
            optional.pop(fallback_id, None)
            rejected[fallback_id] = _incompatible(
                "invalid-pack",
                f"duplicate optional pack id: {fallback_id}",
            )
            continue
        claimed_optional_ids.add(fallback_id)
        try:
            manifest = validate_pack_manifest(document, inventory)
            if manifest["kind"] != "radio":
                raise ContentPackError("an optional pack must use the radio kind")
            pack_id = manifest["id"]
            optional[pack_id] = manifest
        except ContentPackError as error:
            rejected[fallback_id] = _incompatible("invalid-pack", str(error))

    installed = {core["id"]: core["version"]}
    installed.update({pack_id: manifest["version"] for pack_id, manifest in optional.items()})
    core_result = evaluate_pack_compatibility(
        core,
        game_version=game_version,
        ruleset_id=ruleset_id,
        ruleset_version=ruleset_version,
        installed_packs=installed,
    )
    if not core_result.compatible:
        for pack_id in optional:
            rejected[pack_id] = _incompatible(
                "core-unavailable",
                f"Pack {pack_id} cannot load because the offline core is incompatible.",
            )
        return PackSetResolution(core_result, (), dict(sorted(rejected.items())))

    accepted = []
    for pack_id, manifest in sorted(optional.items()):
        result = evaluate_pack_compatibility(
            manifest,
            game_version=game_version,
            ruleset_id=ruleset_id,
            ruleset_version=ruleset_version,
            installed_packs=installed,
        )
        if result.compatible:
            accepted.append(pack_id)
        else:
            rejected[pack_id] = result
    return PackSetResolution(
        core_result,
        tuple(accepted),
        dict(sorted(rejected.items())),
    )


def _validate_compatibility(value: Any) -> None:
    compatibility = _require_object(value, "content pack compatibility")
    _require_exact_fields(
        compatibility,
        _COMPATIBILITY_FIELDS,
        "content pack compatibility",
    )
    game_version = _require_object(
        compatibility["gameVersion"],
        "content pack gameVersion",
    )
    _require_exact_fields(
        game_version,
        _VERSION_RANGE_FIELDS,
        "content pack gameVersion",
    )
    _validate_semver_range(game_version, "content pack gameVersion")

    ruleset = _require_object(
        compatibility["ruleset"],
        "content pack ruleset",
    )
    _require_exact_fields(
        ruleset,
        _RULESET_RANGE_FIELDS,
        "content pack ruleset",
    )
    _require_identifier(ruleset["id"], "content pack ruleset id")
    minimum = _require_positive_int(
        ruleset["minInclusive"],
        "content pack minimum rules version",
    )
    maximum = _require_positive_int(
        ruleset["maxExclusive"],
        "content pack maximum rules version",
    )
    if minimum >= maximum:
        raise ContentPackError("content pack ruleset range must be non-empty")


def _validate_inventory_binding(
    value: Any,
    inventory: Mapping[str, Any],
) -> None:
    binding = _require_object(value, "content pack inventory")
    _require_exact_fields(
        binding,
        _INVENTORY_BINDING_FIELDS,
        "content pack inventory",
    )
    if type(binding["schemaVersion"]) is not int or binding["schemaVersion"] != CONTENT_INVENTORY_SCHEMA_VERSION:
        raise ContentPackError(f"unsupported bound inventory schema: {binding['schemaVersion']}")
    if binding["schemaVersion"] != inventory.get("schemaVersion"):
        raise ContentPackError("content pack inventory schema does not match inventory")
    if binding["assetRoot"] != inventory.get("assetRoot"):
        raise ContentPackError("content pack asset root does not match inventory")
    if not _LOWER_SHA256.fullmatch(str(binding["policySha256"])):
        raise ContentPackError("content pack policySha256 must be lowercase SHA-256")
    if binding["policySha256"] != inventory.get("policySha256"):
        raise ContentPackError("content pack policy hash does not match inventory")


def _validate_dependencies(value: Any, pack_id: str) -> list[dict[str, Any]]:
    dependencies = _require_list(value, "content pack dependencies")
    seen = set()
    for index, dependency_value in enumerate(dependencies):
        location = f"content pack dependency {index}"
        dependency = _require_object(dependency_value, location)
        _require_exact_fields(dependency, _DEPENDENCY_FIELDS, location)
        dependency_id = _require_identifier(dependency["id"], f"{location} id")
        if dependency_id == pack_id:
            raise ContentPackError("a content pack cannot depend on itself")
        if dependency_id in seen:
            raise ContentPackError(f"duplicate content pack dependency: {dependency_id}")
        seen.add(dependency_id)
        _validate_semver_range(dependency, location)
    return dependencies


def _validate_credits(value: Any) -> dict[str, dict[str, Any]]:
    credit_values = _require_list(value, "content pack credits")
    credits = {}
    for index, credit_value in enumerate(credit_values):
        location = f"content pack credit {index}"
        credit = _require_object(credit_value, location)
        _require_exact_fields(credit, _CREDIT_FIELDS, location)
        credit_id = _require_identifier(credit["id"], f"{location} id")
        if credit_id in credits:
            raise ContentPackError(f"duplicate content pack credit: {credit_id}")
        for field in sorted(_CREDIT_FIELDS - {"id"}):
            _require_text(credit[field], f"{location} {field}")
        credits[credit_id] = credit
    if not credits:
        raise ContentPackError("content pack credits must not be empty")
    return credits


def _validate_files(
    value: Any,
    credits: Mapping[str, Mapping[str, Any]],
) -> list[dict[str, Any]]:
    files = _require_list(value, "content pack files")
    if not files:
        raise ContentPackError("content pack files must not be empty")
    seen_ids = set()
    seen_paths: dict[str, str] = {}
    for index, file_value in enumerate(files):
        location = f"content pack file {index}"
        entry = _require_object(file_value, location)
        _require_exact_fields(entry, _FILE_FIELDS, location)
        file_id = _require_text(entry["id"], f"{location} id")
        if not file_id.startswith("asset:"):
            raise ContentPackError(f"{location} id must start with asset:")
        if file_id in seen_ids:
            raise ContentPackError(f"duplicate content pack file id: {file_id}")
        seen_ids.add(file_id)
        path = _require_relative_path(entry["path"], f"{location} path")
        if file_id != f"asset:{path}":
            raise ContentPackError(f"{location} id does not match its path")
        folded = path.casefold()
        if folded in seen_paths:
            raise ContentPackError(f"content pack paths collide by case: {seen_paths[folded]} and {path}")
        seen_paths[folded] = path
        _require_text(entry["mediaType"], f"{location} mediaType")
        _require_positive_int(entry["bytes"], f"{location} bytes")
        if not _LOWER_SHA256.fullmatch(str(entry["sha256"])):
            raise ContentPackError(f"{location} sha256 must be lowercase SHA-256")
        _require_text(entry["role"], f"{location} role")
        runtime_use = _require_text(entry["runtimeUse"], f"{location} runtimeUse")
        if runtime_use not in _RUNTIME_USES:
            raise ContentPackError(f"{location} has invalid runtimeUse: {runtime_use}")
        credit_id = _require_identifier(entry["creditId"], f"{location} creditId")
        if credit_id not in credits:
            raise ContentPackError(f"{location} references unknown credit: {credit_id}")
    return files


def _validate_radio(
    value: Any,
    kind: str,
    pack_id: str,
    files: Sequence[Mapping[str, Any]],
) -> dict[str, Any] | None:
    if kind == "core":
        if value is not None:
            raise ContentPackError("the core pack radio value must be null")
        return None
    radio = _require_object(value, "content pack radio")
    _require_exact_fields(radio, _RADIO_FIELDS, "content pack radio")
    station_id = _require_identifier(radio["stationId"], "content pack stationId")
    if pack_id != f"vibesnake.radio.{station_id}":
        raise ContentPackError("radio pack id must match its stationId")
    _require_text(radio["stationName"], "content pack stationName")
    track_ids = _require_list(radio["trackIds"], "content pack trackIds")
    if not track_ids:
        raise ContentPackError("a radio pack must contain at least one track")
    validated_track_ids = [
        _require_text(track_id, f"content pack trackIds {index}") for index, track_id in enumerate(track_ids)
    ]
    if len(validated_track_ids) != len(set(validated_track_ids)):
        raise ContentPackError("radio trackIds must be unique")
    files_by_id = {entry["id"]: entry for entry in files}
    for track_id in validated_track_ids:
        track = files_by_id.get(track_id)
        if track is None:
            raise ContentPackError(f"radio track is not in pack files: {track_id}")
        if track["mediaType"] != "audio/mpeg" or track["role"] != "radio-track":
            raise ContentPackError(f"radio track must be audio/mpeg with role radio-track: {track_id}")
    return radio


def _validate_files_against_inventory(
    manifest: Mapping[str, Any],
    inventory: Mapping[str, Any],
    files: Sequence[Mapping[str, Any]],
    credits: Mapping[str, Mapping[str, Any]],
) -> None:
    assets = inventory.get("assets")
    if not isinstance(assets, list):
        raise ContentPackError("content inventory assets must be an array")
    inventory_by_id = {entry.get("id"): entry for entry in assets if isinstance(entry, Mapping)}
    manifest_ids = {entry["id"] for entry in files}
    eligible_ids = {
        entry.get("id")
        for entry in assets
        if isinstance(entry, Mapping) and entry.get("packId") == manifest["id"] and entry.get("exportEligible") is True
    }
    if manifest_ids != eligible_ids:
        missing = sorted(eligible_ids - manifest_ids)
        unexpected = sorted(manifest_ids - eligible_ids)
        details = []
        if missing:
            details.append(f"missing {', '.join(missing)}")
        if unexpected:
            details.append(f"unexpected {', '.join(unexpected)}")
        raise ContentPackError(
            "content pack files do not equal the approved inventory allowlist: " + "; ".join(details)
        )

    compared_fields = {
        "id": "id",
        "path": "path",
        "mediaType": "mediaType",
        "bytes": "bytes",
        "sha256": "sha256",
        "role": "role",
        "runtimeUse": "runtimeUse",
    }
    for file_entry in files:
        inventory_entry = inventory_by_id[file_entry["id"]]
        for manifest_field, inventory_field in compared_fields.items():
            if file_entry[manifest_field] != inventory_entry.get(inventory_field):
                raise ContentPackError(f"pack file {file_entry['id']} {manifest_field} does not match inventory")
        if (
            inventory_entry.get("packId") != manifest["id"]
            or inventory_entry.get("shipStatus") != "approved"
            or inventory_entry.get("exportEligible") is not True
            or inventory_entry.get("integrityStatus") != "valid"
            or inventory_entry.get("duplicateOf") is not None
        ):
            raise ContentPackError(f"pack file is not an approved unique valid export: {file_entry['id']}")
        rights = inventory_entry.get("rights")
        if not isinstance(rights, Mapping) or rights.get("status") != "cleared":
            raise ContentPackError(f"pack file does not have cleared rights: {file_entry['id']}")
        credit = credits[file_entry["creditId"]]
        expected_credit = {
            "source": rights.get("source"),
            "license": rights.get("license"),
            "attribution": rights.get("attribution"),
            "reviewEvidence": rights.get("reviewNote"),
        }
        for field, expected in expected_credit.items():
            if credit[field] != expected:
                raise ContentPackError(f"pack credit {credit['id']} {field} does not match inventory rights")


def _require_object(value: Any, location: str) -> dict[str, Any]:
    if not isinstance(value, Mapping):
        raise ContentPackError(f"{location} must be a JSON object")
    return dict(value)


def _require_list(value: Any, location: str) -> list[Any]:
    if not isinstance(value, list):
        raise ContentPackError(f"{location} must be a JSON array")
    return value


def _require_exact_fields(
    value: Mapping[str, Any],
    expected: set[str],
    location: str,
) -> None:
    actual = set(value)
    if actual == expected:
        return
    missing = sorted(expected - actual)
    unknown = sorted(actual - expected)
    details = []
    if missing:
        details.append(f"missing {', '.join(missing)}")
    if unknown:
        details.append(f"unknown {', '.join(unknown)}")
    raise ContentPackError(f"{location} has invalid fields: {'; '.join(details)}")


def _require_text(value: Any, location: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ContentPackError(f"{location} must be a non-empty string")
    return value


def _require_identifier(value: Any, location: str) -> str:
    text = _require_text(value, location)
    if not _IDENTIFIER.fullmatch(text):
        raise ContentPackError(f"{location} must use lowercase letters, numbers, dots, or hyphens")
    return text


def _require_relative_path(value: Any, location: str) -> str:
    text = _require_text(value, location)
    if "\\" in text or text.startswith("/"):
        raise ContentPackError(f"{location} must use a relative POSIX path")
    parts = PurePosixPath(text).parts
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ContentPackError(f"{location} contains an unsafe path segment")
    return text


def _require_positive_int(value: Any, location: str) -> int:
    if type(value) is not int or value <= 0:
        raise ContentPackError(f"{location} must be a positive integer")
    return value


def _parse_semver(value: Any, location: str) -> tuple[int, int, int]:
    text = _require_text(value, location)
    match = _SEMVER.fullmatch(text)
    if match is None:
        raise ContentPackError(f"{location} must use MAJOR.MINOR.PATCH")
    return tuple(int(part) for part in match.groups())


def _validate_semver_range(value: Mapping[str, Any], location: str) -> None:
    minimum = _parse_semver(value["minInclusive"], f"{location} minInclusive")
    maximum = _parse_semver(value["maxExclusive"], f"{location} maxExclusive")
    if minimum >= maximum:
        raise ContentPackError(f"{location} must define a non-empty version range")


def _incompatible(code: str, message: str) -> PackCompatibilityResult:
    return PackCompatibilityResult(False, code, message)


def _unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value = {}
    for key, item in pairs:
        if key in value:
            raise ContentPackError(f"content pack contains duplicate JSON field: {key}")
        value[key] = item
    return value
