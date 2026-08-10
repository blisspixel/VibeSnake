"""Validate and assemble an unsigned native GitHub alpha preview."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path
from typing import Any

try:
    from product_version import read_product_version
except ModuleNotFoundError:  # Imported as scripts.assemble_unsigned_preview in tests.
    from scripts.product_version import read_product_version


PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
ALPHA_PATTERN = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-alpha\.([1-9][0-9]*)")
EXTENSIONS = {
    "windows-x64": ".zip",
    "macos-universal": ".zip",
    "linux-x64": ".tar.gz",
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _read_json(path: Path, label: str, errors: list[str]) -> Any | None:
    if not path.is_file():
        errors.append(f"missing {label}: {path}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        errors.append(f"unreadable {label}: {path}: {error}")
        return None


def _expect(document: dict[str, Any], field: str, expected: Any, label: str, errors: list[str]) -> None:
    actual = document.get(field)
    if actual != expected:
        errors.append(f"{label}.{field} must be {expected!r}; got {actual!r}")


def _parse_checksums(path: Path, errors: list[str]) -> dict[str, str]:
    checksums: dict[str, str] = {}
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as error:
        errors.append(f"unreadable qualification checksums: {path}: {error}")
        return checksums
    for index, line in enumerate(lines, start=1):
        match = re.fullmatch(r"([0-9a-f]{64}) \*([^/\\]+)", line)
        if match is None:
            errors.append(f"qualification checksum line {index} is malformed")
            continue
        digest, name = match.groups()
        if name in checksums:
            errors.append(f"qualification checksums repeat {name}")
            continue
        checksums[name] = digest
    return checksums


def _platform_rows(matrix: dict[str, Any], errors: list[str]) -> dict[str, dict[str, Any]]:
    value = matrix.get("platforms")
    if not isinstance(value, list) or len(value) != len(PLATFORMS):
        errors.append("release matrix must contain exactly three platform rows")
        return {}
    rows: dict[str, dict[str, Any]] = {}
    for index, row in enumerate(value):
        if not isinstance(row, dict):
            errors.append(f"release matrix platform row {index} must be an object")
            continue
        platform = row.get("platform")
        if platform not in PLATFORMS or platform in rows:
            errors.append(f"release matrix platform row {index} must have a unique supported platform")
            continue
        rows[platform] = row
    return rows


def _one_provenance_file(root: Path, platform: str, errors: list[str]) -> Path | None:
    artifact_root = root / f"vibesnake-{platform}-provenance"
    files = sorted(path for path in artifact_root.rglob("*") if path.is_file())
    if len(files) != 1 or files[0].stat().st_size == 0 or files[0].suffix != ".jsonl":
        errors.append(f"{platform} provenance artifact must contain exactly one nonempty JSONL file")
        return None
    return files[0]


def assemble_unsigned_preview(
    channel_root: Path,
    provenance_root: Path,
    matrix_path: Path,
    version_root: Path,
    tag_name: str,
    expected_revision: str,
    output_root: Path,
) -> tuple[list[str], dict[str, Any]]:
    """Validate qualified inputs and create an explicitly unsigned alpha bundle."""
    errors: list[str] = []
    try:
        product_version = read_product_version(version_root)
    except ValueError as error:
        errors.append(str(error))
        product_version = ""
    if not ALPHA_PATTERN.fullmatch(product_version):
        errors.append("unsigned preview publication requires a canonical alpha product version")
    if tag_name != f"v{product_version}":
        errors.append(f"tag must exactly match canonical product version v{product_version}")
    if not REVISION_PATTERN.fullmatch(expected_revision):
        errors.append("expected revision must be a lowercase 40-character Git revision")

    matrix = _read_json(matrix_path, "release matrix", errors)
    rows: dict[str, dict[str, Any]] = {}
    if isinstance(matrix, dict):
        _expect(matrix, "schemaVersion", 1, "matrix", errors)
        _expect(matrix, "kind", "release-matrix-qualification-v1", "matrix", errors)
        _expect(matrix, "passed", True, "matrix", errors)
        _expect(matrix, "sourceRevision", expected_revision, "matrix", errors)
        _expect(matrix, "buildMode", "Release", "matrix", errors)
        _expect(matrix, "productVersion", product_version, "matrix", errors)
        _expect(matrix, "publicationEligible", False, "matrix", errors)
        rows = _platform_rows(matrix, errors)

    prepared: list[dict[str, Any]] = []
    for platform in PLATFORMS:
        input_root = channel_root / f"vibesnake-{platform}-unsigned-channel-shape"
        plan_path = input_root / "release_output_plan.json"
        manifest_path = input_root / "artifact-manifest.json"
        checksums_path = input_root / "SHA256SUMS"
        plan = _read_json(plan_path, f"{platform} output plan", errors)
        manifest = _read_json(manifest_path, f"{platform} artifact manifest", errors)
        package_path: Path | None = None
        expected_qualification_name = f"VibeSnake-{product_version}-{platform}-qualification{EXTENSIONS[platform]}"
        if isinstance(plan, dict):
            for field, expected in (
                ("schemaVersion", 1),
                ("kind", "release-output-plan-v1"),
                ("product", "Vibe Snake"),
                ("productVersion", product_version),
                ("platform", platform),
                ("directDownloadFileName", expected_qualification_name),
                ("passed", True),
                ("qualificationOnly", True),
                ("assemblyEligible", True),
                ("publicationEligible", False),
                ("optionalPackOutputSeparate", True),
                ("baseGameIncludesOptionalPacks", False),
                ("playerDataExcluded", True),
                ("uninstallPreservesPlayerData", True),
                ("deterministicRepeatMatched", True),
            ):
                _expect(plan, field, expected, f"{platform} plan", errors)
            package_path = input_root / expected_qualification_name
            package_bytes = plan.get("packageBytes")
            package_sha = plan.get("packageSha256")
            if not isinstance(package_bytes, int) or isinstance(package_bytes, bool) or package_bytes <= 0:
                errors.append(f"{platform} plan.packageBytes must be a positive integer")
            if not SHA256_PATTERN.fullmatch(str(package_sha)):
                errors.append(f"{platform} plan.packageSha256 must be a SHA-256 digest")
            if not package_path.is_file():
                errors.append(f"missing {platform} qualification package: {package_path}")
            else:
                if package_path.stat().st_size != package_bytes:
                    errors.append(f"{platform} qualification package byte count changed")
                if _sha256(package_path) != package_sha:
                    errors.append(f"{platform} qualification package hash changed")
            row = rows.get(platform)
            if row is not None:
                for field, expected in (
                    ("packageSha256", package_sha),
                    ("packageBytes", package_bytes),
                    ("directDownloadFileName", expected_qualification_name),
                ):
                    _expect(row, field, expected, f"{platform} matrix row", errors)

        if isinstance(manifest, dict):
            for field, expected in (
                ("schemaVersion", 2),
                ("product", "Vibe Snake"),
                ("platform", platform),
                ("buildMode", "Release"),
                ("sourceRevision", expected_revision),
            ):
                _expect(manifest, field, expected, f"{platform} artifact manifest", errors)
            row = rows.get(platform)
            if row is not None:
                _expect(
                    row,
                    "artifactManifestSha256",
                    _sha256(manifest_path),
                    f"{platform} matrix row",
                    errors,
                )

        checksums = _parse_checksums(checksums_path, errors)
        expected_checksum_names = {expected_qualification_name, plan_path.name, manifest_path.name}
        if set(checksums) != expected_checksum_names:
            errors.append(f"{platform} qualification checksums must cover exactly the package and manifests")
        for file_name, digest in checksums.items():
            target = input_root / file_name
            if not target.is_file() or _sha256(target) != digest:
                errors.append(f"{platform} qualification checksum mismatch for {file_name}")
        actual_names = (
            {path.relative_to(input_root).as_posix() for path in input_root.rglob("*") if path.is_file()}
            if input_root.is_dir()
            else set()
        )
        if actual_names != expected_checksum_names | {checksums_path.name}:
            errors.append(f"{platform} channel-shape artifact contains an unexpected file set")

        provenance_path = _one_provenance_file(provenance_root, platform, errors)
        if (
            isinstance(plan, dict)
            and isinstance(manifest, dict)
            and package_path is not None
            and package_path.is_file()
            and provenance_path is not None
        ):
            prepared.append(
                {
                    "platform": platform,
                    "sourcePackage": package_path,
                    "sourcePackageName": expected_qualification_name,
                    "packageSha256": plan.get("packageSha256"),
                    "packageBytes": plan.get("packageBytes"),
                    "provenance": provenance_path,
                    "artifactManifestSha256": _sha256(manifest_path),
                    "outputPlanSha256": _sha256(plan_path),
                }
            )

    evidence: dict[str, Any] = {
        "schemaVersion": 1,
        "kind": "unsigned-native-alpha-preview-v1",
        "passed": not errors,
        "product": "Vibe Snake",
        "productVersion": product_version,
        "tagName": tag_name,
        "sourceRevision": expected_revision,
        "buildMode": "Release",
        "channel": "github-prerelease",
        "unsigned": True,
        "stablePublicationEligible": False,
        "optionalPackOutputSeparate": True,
        "baseGameIncludesOptionalPacks": False,
        "releaseMatrixSha256": _sha256(matrix_path) if matrix_path.is_file() else None,
        "packages": [],
        "knownLimitations": [
            "Windows and macOS packages are unsigned.",
            "macOS Gatekeeper may require an explicit local override.",
            "The separately approved radio pack is not part of the base-game archives.",
            "Alpha save and replay compatibility may change before stable release.",
        ],
        "errors": errors,
    }
    if errors:
        return errors, evidence
    if output_root.exists():
        error = f"preview output must not already exist: {output_root}"
        evidence["passed"] = False
        evidence["errors"] = [error]
        return [error], evidence

    try:
        output_root.mkdir(parents=True)
        package_rows: list[dict[str, Any]] = []
        checksum_rows: list[tuple[str, str]] = []
        for item in prepared:
            platform = item["platform"]
            preview_name = f"VibeSnake-{product_version}-{platform}-unsigned-preview{EXTENSIONS[platform]}"
            preview_path = output_root / preview_name
            shutil.copyfile(item["sourcePackage"], preview_path)
            package_digest = _sha256(preview_path)
            if package_digest != item["packageSha256"] or preview_path.stat().st_size != item["packageBytes"]:
                raise OSError(f"copied preview package changed for {platform}")
            provenance_name = f"VibeSnake-{product_version}-{platform}-provenance.jsonl"
            provenance_path = output_root / provenance_name
            shutil.copyfile(item["provenance"], provenance_path)
            provenance_digest = _sha256(provenance_path)
            package_rows.append(
                {
                    "platform": platform,
                    "fileName": preview_name,
                    "bytes": preview_path.stat().st_size,
                    "sha256": package_digest,
                    "sourceQualificationFileName": item["sourcePackageName"],
                    "artifactManifestSha256": item["artifactManifestSha256"],
                    "outputPlanSha256": item["outputPlanSha256"],
                    "provenanceFileName": provenance_name,
                    "provenanceSha256": provenance_digest,
                }
            )
            checksum_rows.extend(((package_digest, preview_name), (provenance_digest, provenance_name)))
        evidence["packages"] = package_rows
        manifest_path = output_root / "unsigned_preview_manifest.json"
        manifest_path.write_text(
            json.dumps(evidence, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        checksum_rows.append((_sha256(manifest_path), manifest_path.name))
        (output_root / "SHA256SUMS.txt").write_text(
            "".join(f"{digest}  {name}\n" for digest, name in sorted(checksum_rows, key=lambda row: row[1])),
            encoding="utf-8",
        )
    except (OSError, UnicodeError) as error:
        shutil.rmtree(output_root, ignore_errors=True)
        message = f"could not assemble preview output: {error}"
        evidence["passed"] = False
        evidence["errors"] = [message]
        return [message], evidence
    return [], evidence


def main(argv: list[str] | None = None) -> int:
    """Assemble a preview or print every validation error."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("channel_root", type=Path)
    parser.add_argument("--provenance-root", type=Path, required=True)
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--version-root", type=Path, default=Path.cwd())
    parser.add_argument("--tag", required=True)
    parser.add_argument("--expected-revision", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    errors, evidence = assemble_unsigned_preview(
        args.channel_root.resolve(),
        args.provenance_root.resolve(),
        args.matrix.resolve(),
        args.version_root.resolve(),
        args.tag,
        args.expected_revision,
        args.output.resolve(),
    )
    if errors:
        print("Unsigned native alpha preview assembly failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    print(
        "Unsigned native alpha preview assembled: "
        f"version={evidence['productVersion']} platforms={len(evidence['packages'])}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
