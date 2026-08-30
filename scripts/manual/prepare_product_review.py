"""Prepare a hash-bound workspace for manual review of one verified Release matrix."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import runpy
import shutil
import sys
from pathlib import Path
from typing import Any, Mapping
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[2]
_MANUAL_MATRIX = runpy.run_path(str(ROOT / "scripts" / "check_manual_product_matrix.py"))
_RELEASE_MATRIX = runpy.run_path(str(ROOT / "scripts" / "check_release_matrix.py"))
PLATFORM_ROWS = _MANUAL_MATRIX["PLATFORM_ROWS"]
REQUIRED_FLOWS = _MANUAL_MATRIX["REQUIRED_FLOWS"]
validate_release_matrix = _RELEASE_MATRIX["validate_release_matrix"]
read_release_json = _RELEASE_MATRIX["_read_json"]


DEFAULT_OUTPUT_ROOT = ROOT / "TestResults" / "manual-product-review"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
RUN_ID_PATTERN = re.compile(r"[1-9][0-9]*")
REPOSITORY_PATTERN = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")
MATRIX_RELATIVE_PATH = Path("vibesnake-release-matrix") / "release_matrix.json"
CANDIDATE_NAME = "candidate.json"
WORKSPACE_MANIFEST_NAME = "workspace-manifest.json"
REVIEW_GUIDE_NAME = "REVIEW.md"


class ProductReviewPreparationError(ValueError):
    """Raised when a manual review workspace cannot be prepared safely."""


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def require_output_root(path: Path) -> Path:
    """Allow ignored TestResults output or a workspace outside the repository."""
    resolved = path.expanduser().resolve()
    test_results = (ROOT / "TestResults").resolve()
    if resolved == ROOT or (resolved.is_relative_to(ROOT) and not resolved.is_relative_to(test_results)):
        raise ProductReviewPreparationError("review output inside the repository must be under ignored TestResults")
    return resolved


def build_candidate(
    matrix: Mapping[str, Any],
    matrix_sha256: str,
    release_run_id: int,
    repository: str,
) -> dict[str, Any]:
    """Project a verified three-platform matrix into the four manual platform rows."""
    if matrix.get("kind") != "release-matrix-qualification-v1" or matrix.get("passed") is not True:
        raise ProductReviewPreparationError("Release matrix must be a passing qualification record")
    if matrix.get("buildMode") != "Release":
        raise ProductReviewPreparationError("manual product review requires a Release matrix")
    revision = matrix.get("sourceRevision")
    version = matrix.get("productVersion")
    if not isinstance(revision, str) or not REVISION_PATTERN.fullmatch(revision):
        raise ProductReviewPreparationError("Release matrix source revision is invalid")
    if not isinstance(version, str) or not version:
        raise ProductReviewPreparationError("Release matrix product version is invalid")
    platform_rows = matrix.get("platforms")
    if not isinstance(platform_rows, list):
        raise ProductReviewPreparationError("Release matrix platforms must be an array")
    by_platform = {str(row.get("platform")): row for row in platform_rows if isinstance(row, Mapping)}
    if set(by_platform) != {row[1] for row in PLATFORM_ROWS}:
        raise ProductReviewPreparationError("Release matrix does not contain the exact manual artifact platforms")

    artifacts: list[dict[str, Any]] = []
    for platform_row_id, artifact_platform, architecture in PLATFORM_ROWS:
        row = by_platform[artifact_platform]
        artifact_sha256 = row.get("packageSha256")
        manifest_sha256 = row.get("artifactManifestSha256")
        file_name = row.get("directDownloadFileName")
        package_bytes = row.get("packageBytes")
        if not isinstance(artifact_sha256, str) or not re.fullmatch(r"[0-9a-f]{64}", artifact_sha256):
            raise ProductReviewPreparationError(f"{artifact_platform} package SHA-256 is invalid")
        if not isinstance(manifest_sha256, str) or not re.fullmatch(r"[0-9a-f]{64}", manifest_sha256):
            raise ProductReviewPreparationError(f"{artifact_platform} manifest SHA-256 is invalid")
        if not isinstance(file_name, str) or not file_name or "/" in file_name or "\\" in file_name:
            raise ProductReviewPreparationError(f"{artifact_platform} download file name is invalid")
        if type(package_bytes) is not int or package_bytes <= 0:
            raise ProductReviewPreparationError(f"{artifact_platform} package size is invalid")
        artifacts.append(
            {
                "platformRowId": platform_row_id,
                "artifactPlatform": artifact_platform,
                "architecture": architecture,
                "fileName": file_name,
                "sha256": artifact_sha256,
                "bytes": package_bytes,
                "artifactManifestSha256": manifest_sha256,
            }
        )

    return {
        "schemaVersion": 1,
        "kind": "vibesnake-manual-product-matrix-candidate-v1",
        "releaseRunId": release_run_id,
        "releaseRunUrl": f"https://github.com/{repository}/actions/runs/{release_run_id}",
        "releaseMatrixSha256": matrix_sha256,
        "candidateRevision": revision,
        "appVersion": version,
        "buildMode": "Release",
        "artifactRows": artifacts,
        "humanReviewStatus": "pending",
        "releaseAcceptance": False,
        "publicationEligible": False,
    }


def build_session_template(candidate: Mapping[str, Any], artifact_row: Mapping[str, Any]) -> dict[str, Any]:
    """Create an intentionally incomplete session template with exact immutable identity fields."""
    platform_row_id = str(artifact_row["platformRowId"])
    return {
        "schemaVersion": 2,
        "kind": "vibesnake-manual-product-matrix-session-v2",
        "sessionId": "product-matrix-REPLACE",
        "candidateRevision": candidate["candidateRevision"],
        "artifactSha256": artifact_row["sha256"],
        "appVersion": candidate["appVersion"],
        "platformRowId": platform_row_id,
        "operatingSystemVersion": "REPLACE",
        "hardwareClass": "REPLACE",
        "renderer": "REPLACE",
        "executedUtc": "REPLACE",
        "results": [
            {
                "flowId": flow_id,
                "inputDeviceId": "REPLACE",
                "inputCapabilityIds": [],
                "settingsProfileIds": [],
                "result": "pending",
                "evidencePaths": [f"evidence/{platform_row_id}/{flow_id}.REPLACE"],
            }
            for flow_id in REQUIRED_FLOWS
        ],
    }


def build_review_guide(candidate: Mapping[str, Any]) -> str:
    """Render concise instructions without claiming that review occurred."""
    artifact_lines = "\n".join(
        f"- `{row['platformRowId']}`: `{row['fileName']}`, `{row['sha256']}`, {row['bytes']} bytes"
        for row in candidate["artifactRows"]
    )
    revision = candidate["candidateRevision"]
    return f"""# Exact Candidate Manual Review Workspace

Status: prepared, physical execution pending.

Candidate revision: `{revision}`
Application version: `{candidate["appVersion"]}`
Release run: {candidate["releaseRunUrl"]}

## Exact artifacts

{artifact_lines}

Before launching an artifact, hash the downloaded file and compare it with the exact value above. Do not
continue with a renamed, rebuilt, or mismatched package.

## Record a session

1. Copy the applicable file from `templates/` into `sessions/` with a unique name such as
   `product-matrix-001.json`.
2. Preserve the candidate revision, application version, platform row, and artifact SHA-256 exactly.
3. Replace every `REPLACE` value. Each result names the one input device that executed that flow, any mouse
   capability demonstrated, and every settings profile active for that observation. Record `pass`, `fail`, or
   `blocked` for each executed flow. Copy a platform template into additional sessions when another device or
   profile must execute the same flow.
4. Put sanitized screenshots, video, logs, and observations under `sessions/evidence/<platform-row>/`. Keep every
   evidence path relative and never record controller serials, accounts, private paths, or unrelated device data.
5. A failure or blocked flow is evidence, not a reason to discard the session.

Validate the retained sessions from the repository root:

```powershell
python scripts/check_manual_product_matrix.py `
  --candidate <workspace>/candidate.json `
  --sessions <workspace>/sessions `
  --output <workspace>/decision.json
```

Review remains incomplete until the validator reports all 144 platform-flow cells, all 432 complete-device
flow cells, all 16 mouse-capability cells, and all 32 platform-profile cells passing. This workspace does not
sign, publish, approve, or modify candidate bytes.
"""


def prepare_workspace(
    release_evidence_root: Path,
    expected_revision: str,
    release_run_id: int,
    repository: str,
    output_root: Path = DEFAULT_OUTPUT_ROOT,
) -> tuple[Path, dict[str, Any]]:
    """Validate exact Release evidence and atomically prepare its manual review workspace."""
    if not REVISION_PATTERN.fullmatch(expected_revision):
        raise ProductReviewPreparationError("expected revision must be a lowercase 40-character Git revision")
    if not RUN_ID_PATTERN.fullmatch(str(release_run_id)):
        raise ProductReviewPreparationError("release run ID must be a positive integer")
    if not REPOSITORY_PATTERN.fullmatch(repository):
        raise ProductReviewPreparationError("repository must use owner/name syntax")
    evidence_root = release_evidence_root.expanduser().resolve()
    errors, matrix = validate_release_matrix(evidence_root, expected_revision, "Release")
    if errors:
        raise ProductReviewPreparationError("Release matrix validation failed: " + "; ".join(errors))
    matrix_path = evidence_root / MATRIX_RELATIVE_PATH
    if not matrix_path.is_file() or matrix_path.is_symlink():
        raise ProductReviewPreparationError(f"missing retained Release matrix: {matrix_path}")
    retained_errors: list[str] = []
    retained_matrix = read_release_json(matrix_path, "retained Release matrix", retained_errors)
    if retained_errors:
        raise ProductReviewPreparationError("retained Release matrix is unreadable: " + "; ".join(retained_errors))
    if retained_matrix != matrix:
        raise ProductReviewPreparationError("retained Release matrix does not match independently recomputed evidence")

    candidate = build_candidate(matrix, _sha256(matrix_path), release_run_id, repository)
    resolved_output_root = require_output_root(output_root)
    final_directory = resolved_output_root / expected_revision
    if final_directory.exists():
        raise ProductReviewPreparationError(f"review workspace already exists: {final_directory}")
    resolved_output_root.mkdir(parents=True, exist_ok=True)
    staging_directory = resolved_output_root / f".{expected_revision}.staging.{uuid4().hex}"
    staging_directory.mkdir()
    try:
        _write_json(staging_directory / CANDIDATE_NAME, candidate)
        templates_directory = staging_directory / "templates"
        sessions_directory = staging_directory / "sessions"
        templates_directory.mkdir()
        sessions_directory.mkdir()
        for artifact_row in candidate["artifactRows"]:
            platform_row_id = artifact_row["platformRowId"]
            _write_json(
                templates_directory / f"{platform_row_id}.session.json.template",
                build_session_template(candidate, artifact_row),
            )
            (sessions_directory / "evidence" / platform_row_id).mkdir(parents=True)
        (staging_directory / REVIEW_GUIDE_NAME).write_text(
            build_review_guide(candidate), encoding="utf-8", newline="\n"
        )
        retained_files = sorted(
            path for path in staging_directory.rglob("*") if path.is_file() and path.name != WORKSPACE_MANIFEST_NAME
        )
        workspace_manifest = {
            "schemaVersion": 1,
            "kind": "vibesnake-manual-product-review-workspace-v1",
            "candidateRevision": expected_revision,
            "humanReviewStatus": "pending",
            "releaseAcceptance": False,
            "files": [
                {
                    "path": path.relative_to(staging_directory).as_posix(),
                    "bytes": path.stat().st_size,
                    "sha256": _sha256(path),
                }
                for path in retained_files
            ],
        }
        _write_json(staging_directory / WORKSPACE_MANIFEST_NAME, workspace_manifest)
        os.replace(staging_directory, final_directory)
        return final_directory, workspace_manifest
    except OSError as error:
        raise ProductReviewPreparationError(f"could not prepare manual review workspace: {error}") from error
    finally:
        if staging_directory.exists():
            shutil.rmtree(staging_directory)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("release_evidence_root", type=Path)
    parser.add_argument("--expected-revision", required=True)
    parser.add_argument("--release-run-id", required=True, type=int)
    parser.add_argument("--repository", default="blisspixel/VibeSnake")
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    args = parser.parse_args(argv)
    try:
        output_directory, manifest = prepare_workspace(
            args.release_evidence_root,
            args.expected_revision,
            args.release_run_id,
            args.repository,
            args.output_root,
        )
    except (ProductReviewPreparationError, OSError) as error:
        print(f"Manual product review preparation failed: {error}", file=sys.stderr)
        return 1
    print(
        f"Manual product review workspace prepared: revision={manifest['candidateRevision']} "
        f"files={len(manifest['files'])} output={output_directory} physical_execution=pending"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
