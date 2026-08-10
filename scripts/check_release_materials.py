"""Validate the V090-09 release-material foundation and exact candidate record."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "release_materials_v1.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?")
REQUIRED_DOCUMENT_PATHS = (
    "README.md",
    "docs/guides/PLAYER_GUIDE.md",
    "docs/guides/ACCESSIBILITY.md",
    "PRIVACY.md",
    "SUPPORT.md",
    "docs/guides/RECOVERY.md",
    "docs/release/KNOWN_ISSUES.md",
    "THIRD_PARTY_NOTICES.md",
    "CREDITS.md",
    "CHANGELOG.md",
)
ARTIFACT_PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
INPUT_DEVICE_IDS = (
    "keyboard",
    "mouse",
    "xbox-layout-controller",
    "playstation-layout-controller",
)
SCREENSHOT_ROLES = (
    "main-menu",
    "classic-gameplay",
    "vibe-gameplay",
    "controls-remapping",
    "accessibility-settings",
    "spectator-and-replay",
)
VIDEO_ROLES = ("gameplay-overview", "accessibility-and-input")
MARKETING_CLAIM_IDS = (
    "native-three-platform-player",
    "offline-core-play",
    "keyboard-mouse-controller",
    "nine-integrated-powers",
    "accessibility-features",
    "local-save-recovery",
    "optional-pack-boundary",
    "no-account-required",
)
OFFLINE_BEHAVIOR_VALUE = "core-play-requires-no-account-or-network"
CANDIDATE_FIELDS = (
    "schemaVersion",
    "kind",
    "sourceRevision",
    "appVersion",
    "artifactManifestSha256ByPlatform",
    "downloadBytesByPlatform",
    "installedBytesByPlatform",
    "supportedOperatingSystemsByPlatform",
    "inputDeviceIds",
    "inputEvidencePathsByDevice",
    "offlineBehavior",
    "saveLocationsByPlatform",
    "coreContentBytes",
    "optionalContentBytes",
    "documentationSha256",
    "screenshotPathsByRole",
    "videoPathsByRole",
    "retainedFileSha256",
    "marketingClaims",
)
MARKETING_CLAIM_FIELDS = ("claimId", "statement", "evidencePaths")
RELEASE_RULES = (
    "Every required document is nonempty and hash-bound to the exact candidate record.",
    "Operating-system support and download and installed sizes are stated separately for every artifact platform.",
    "Keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller claims link to retained physical evidence.",
    "Core and optional content sizes are stated separately and match the candidate manifests.",
    "Offline behavior and platform save locations are published exactly.",
    "Every screenshot and video role is captured from the exact candidate and retained as a nonempty file.",
    "Every permitted marketing claim is nonempty, evidence-linked, and bound to the candidate revision.",
    "Pending, reference-player, qualification-only, or unapproved-content evidence cannot be presented as a final candidate claim.",
)
PENDING_DOCUMENT_MARKERS = {
    "README.md": ("Store-ready 1.0 is not ready",),
    "docs/guides/PLAYER_GUIDE.md": ("currently runs from a source checkout",),
    "docs/guides/ACCESSIBILITY.md": ("Accessibility validation is still in progress",),
    "PRIVACY.md": ("Final candidate review is pending",),
    "SUPPORT.md": ("Public support, issue, play-feedback, and enhancement intake is currently closed",),
    "docs/guides/RECOVERY.md": ("final candidate wording and physical review are pending",),
    "docs/release/KNOWN_ISSUES.md": ("pre-candidate alpha issues",),
    "THIRD_PARTY_NOTICES.md": ("final notice bundle must be regenerated",),
    "CREDITS.md": ("Final candidate content and platform credits are pending",),
}


def _read_json(path: Path, label: str, errors: list[str]) -> Any | None:
    if not path.is_file():
        errors.append(f"missing {label}: {path}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"unreadable {label}: {path}: {exc}")
        return None


def _strict_keys(value: Any, expected: set[str], label: str, errors: list[str]) -> bool:
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object")
        return False
    if set(value) != expected:
        errors.append(f"{label} fields must be {sorted(expected)!r}; got {sorted(value)!r}")
        return False
    return True


def _exact(value: Any, expected: Any, label: str, errors: list[str]) -> None:
    if value != expected:
        errors.append(f"{label} must be {expected!r}; got {value!r}")


def _nonempty_string(value: Any, label: str, errors: list[str]) -> bool:
    valid = isinstance(value, str) and bool(value.strip())
    if not valid:
        errors.append(f"{label} must be a nonempty string")
    return valid


def _safe_relative_path(value: Any) -> bool:
    if not isinstance(value, str) or not value or "\\" in value:
        return False
    path = Path(value)
    return not path.is_absolute() and ".." not in path.parts


def _existing_relative_paths(value: Any, base: Path, label: str, errors: list[str]) -> list[str]:
    if (
        not isinstance(value, list)
        or not value
        or not all(isinstance(item, str) for item in value)
        or len(value) != len(set(value))
        or not all(_safe_relative_path(item) for item in value)
    ):
        errors.append(f"{label} must contain unique safe relative paths")
        return []
    missing = [item for item in value if not (base / item).is_file() or (base / item).stat().st_size == 0]
    if missing:
        errors.append(f"{label} reference missing or empty retained files: {', '.join(missing)}")
        return []
    return list(value)


def validate_contract(contract_path: Path = CONTRACT_PATH) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact release-material contract."""
    errors: list[str] = []
    contract = _read_json(contract_path, "release materials contract", errors)
    expected_fields = {
        "schemaVersion",
        "kind",
        "status",
        "requiredDocumentPaths",
        "artifactPlatforms",
        "inputDeviceIds",
        "screenshotRoles",
        "videoRoles",
        "marketingClaimIds",
        "offlineBehaviorValue",
        "requiredCandidateFields",
        "requiredMarketingClaimFields",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None
    _exact(contract["schemaVersion"], 1, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-release-materials-v1", "contract.kind", errors)
    _exact(
        contract["status"],
        "foundation-qualified-candidate-pending",
        "contract.status",
        errors,
    )
    exact_lists = (
        ("requiredDocumentPaths", REQUIRED_DOCUMENT_PATHS),
        ("artifactPlatforms", ARTIFACT_PLATFORMS),
        ("inputDeviceIds", INPUT_DEVICE_IDS),
        ("screenshotRoles", SCREENSHOT_ROLES),
        ("videoRoles", VIDEO_ROLES),
        ("marketingClaimIds", MARKETING_CLAIM_IDS),
        ("requiredCandidateFields", CANDIDATE_FIELDS),
        ("requiredMarketingClaimFields", MARKETING_CLAIM_FIELDS),
        ("releaseRules", RELEASE_RULES),
    )
    for field, expected in exact_lists:
        _exact(contract[field], list(expected), f"contract.{field}", errors)
    _exact(contract["offlineBehaviorValue"], OFFLINE_BEHAVIOR_VALUE, "contract.offlineBehaviorValue", errors)
    return errors, contract


def _document_hashes(documents_root: Path, errors: list[str]) -> dict[str, str]:
    hashes: dict[str, str] = {}
    for relative_path in REQUIRED_DOCUMENT_PATHS:
        path = documents_root / relative_path
        if not path.is_file():
            errors.append(f"missing required release document: {relative_path}")
            continue
        contents = path.read_bytes()
        if len(contents) < 200:
            errors.append(f"required release document is unexpectedly small: {relative_path}")
            continue
        hashes[relative_path] = hashlib.sha256(contents).hexdigest()
    return hashes


def _canonical_app_version(documents_root: Path, errors: list[str]) -> str | None:
    pyproject_path = documents_root / "pyproject.toml"
    if not pyproject_path.is_file():
        errors.append(f"missing canonical project metadata: {pyproject_path}")
        return None
    match = re.search(
        r'(?m)^version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)"\s*$',
        pyproject_path.read_text(encoding="utf-8"),
    )
    if match is None:
        errors.append("could not resolve the canonical application version")
        return None
    return match.group(1)


def _validate_media_bytes(path: Path, media_kind: str, label: str, errors: list[str]) -> None:
    payload = path.read_bytes() if path.is_file() else b""
    if media_kind == "image":
        valid = payload.startswith(b"\x89PNG\r\n\x1a\n") or payload.startswith(b"\xff\xd8\xff")
    else:
        valid = (len(payload) >= 12 and payload[4:8] == b"ftyp") or payload.startswith(b"\x1aE\xdf\xa3")
    if not valid:
        errors.append(f"{label} is not a recognized retained {media_kind} file: {path}")


def _positive_integer_map(value: Any, label: str, errors: list[str]) -> None:
    if not _strict_keys(value, set(ARTIFACT_PLATFORMS), label, errors):
        return
    for platform, size in value.items():
        if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
            errors.append(f"{label}.{platform} must be a positive integer byte count")


def _validate_candidate(
    path: Path,
    document_hashes: dict[str, str],
    canonical_app_version: str | None,
    expected_revision: str,
    errors: list[str],
) -> dict[str, Any] | None:
    candidate = _read_json(path, "release materials candidate", errors)
    if not _strict_keys(candidate, set(CANDIDATE_FIELDS), "candidate", errors):
        return None
    _exact(candidate["schemaVersion"], 1, "candidate.schemaVersion", errors)
    _exact(candidate["kind"], "vibesnake-release-materials-candidate-v1", "candidate.kind", errors)
    if not REVISION_PATTERN.fullmatch(str(candidate["sourceRevision"])):
        errors.append("candidate.sourceRevision must be a lowercase 40-character revision")
    _exact(candidate["sourceRevision"], expected_revision, "candidate.sourceRevision", errors)
    if not VERSION_PATTERN.fullmatch(str(candidate["appVersion"])):
        errors.append("candidate.appVersion must be a semantic application version")
    if canonical_app_version is not None:
        _exact(candidate["appVersion"], canonical_app_version, "candidate.appVersion", errors)
    manifests = candidate["artifactManifestSha256ByPlatform"]
    if _strict_keys(manifests, set(ARTIFACT_PLATFORMS), "candidate artifact manifests", errors):
        if not all(SHA256_PATTERN.fullmatch(str(value)) for value in manifests.values()):
            errors.append("candidate artifact manifests must contain SHA-256 digests")
    _positive_integer_map(candidate["downloadBytesByPlatform"], "candidate.downloadBytesByPlatform", errors)
    _positive_integer_map(candidate["installedBytesByPlatform"], "candidate.installedBytesByPlatform", errors)
    os_support = candidate["supportedOperatingSystemsByPlatform"]
    if _strict_keys(os_support, set(ARTIFACT_PLATFORMS), "candidate operating systems", errors):
        for platform, versions in os_support.items():
            if (
                not isinstance(versions, list)
                or not versions
                or not all(isinstance(item, str) and item.strip() for item in versions)
                or len(versions) != len(set(versions))
            ):
                errors.append(f"candidate operating systems for {platform} must be unique nonempty values")
    _exact(candidate["inputDeviceIds"], list(INPUT_DEVICE_IDS), "candidate.inputDeviceIds", errors)
    retained_paths: set[str] = set()
    input_evidence = candidate["inputEvidencePathsByDevice"]
    if _strict_keys(input_evidence, set(INPUT_DEVICE_IDS), "candidate input evidence", errors):
        for device_id in INPUT_DEVICE_IDS:
            values = _existing_relative_paths(
                input_evidence[device_id], path.parent, f"candidate input evidence.{device_id}", errors
            )
            retained_paths.update(values)
    _exact(candidate["offlineBehavior"], OFFLINE_BEHAVIOR_VALUE, "candidate.offlineBehavior", errors)
    save_locations = candidate["saveLocationsByPlatform"]
    if _strict_keys(save_locations, set(ARTIFACT_PLATFORMS), "candidate save locations", errors):
        for platform, location in save_locations.items():
            _nonempty_string(location, f"candidate save locations.{platform}", errors)
    for field in ("coreContentBytes", "optionalContentBytes"):
        size = candidate[field]
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            errors.append(f"candidate.{field} must be a nonnegative integer byte count")
    _exact(candidate["documentationSha256"], document_hashes, "candidate.documentationSha256", errors)
    screenshots = candidate["screenshotPathsByRole"]
    if _strict_keys(screenshots, set(SCREENSHOT_ROLES), "candidate screenshots", errors):
        for role in SCREENSHOT_ROLES:
            values = _existing_relative_paths(screenshots[role], path.parent, f"candidate screenshots.{role}", errors)
            if any(Path(value).suffix.lower() not in {".png", ".jpg", ".jpeg"} for value in values):
                errors.append(f"candidate screenshots.{role} must use PNG or JPEG files")
            for value in values:
                _validate_media_bytes(path.parent / value, "image", f"candidate screenshots.{role}", errors)
            retained_paths.update(values)
    videos = candidate["videoPathsByRole"]
    if _strict_keys(videos, set(VIDEO_ROLES), "candidate videos", errors):
        for role in VIDEO_ROLES:
            values = _existing_relative_paths(videos[role], path.parent, f"candidate videos.{role}", errors)
            if any(Path(value).suffix.lower() not in {".mp4", ".webm"} for value in values):
                errors.append(f"candidate videos.{role} must use MP4 or WebM files")
            for value in values:
                _validate_media_bytes(path.parent / value, "video", f"candidate videos.{role}", errors)
            retained_paths.update(values)
    claims = candidate["marketingClaims"]
    seen_claims: set[str] = set()
    if not isinstance(claims, list):
        errors.append("candidate.marketingClaims must be an array")
    else:
        for index, claim in enumerate(claims):
            label = f"candidate.marketingClaims[{index}]"
            if not _strict_keys(claim, set(MARKETING_CLAIM_FIELDS), label, errors):
                continue
            claim_id = claim["claimId"] if isinstance(claim["claimId"], str) else ""
            if claim_id not in MARKETING_CLAIM_IDS or claim_id in seen_claims:
                errors.append(f"{label}.claimId must be unique and supported")
            seen_claims.add(claim_id)
            _nonempty_string(claim["statement"], f"{label}.statement", errors)
            values = _existing_relative_paths(claim["evidencePaths"], path.parent, f"{label}.evidencePaths", errors)
            retained_paths.update(values)
    if seen_claims != set(MARKETING_CLAIM_IDS):
        errors.append("candidate.marketingClaims must cover every permitted claim")
    retained_hashes = candidate["retainedFileSha256"]
    _strict_keys(retained_hashes, retained_paths, "candidate.retainedFileSha256", errors)
    if isinstance(retained_hashes, dict):
        for relative_path, expected_sha in retained_hashes.items():
            if not _safe_relative_path(relative_path):
                errors.append(f"candidate.retainedFileSha256 contains an unsafe path: {relative_path}")
                continue
            if not SHA256_PATTERN.fullmatch(str(expected_sha)):
                errors.append(f"candidate.retainedFileSha256.{relative_path} must be a SHA-256 digest")
                continue
            retained_path = path.parent / relative_path
            if not retained_path.is_file():
                continue
            actual_sha = hashlib.sha256(retained_path.read_bytes()).hexdigest()
            if actual_sha != expected_sha:
                errors.append(f"candidate retained file hash mismatch: {relative_path}")
    return candidate


def validate_release_materials(
    contract_path: Path = CONTRACT_PATH,
    candidate_path: Path | None = None,
    expected_revision: str | None = None,
    documents_root: Path = ROOT,
) -> tuple[list[str], dict[str, Any]]:
    """Validate release-material foundation and optional final candidate record."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    document_hashes = _document_hashes(documents_root, errors)
    canonical_app_version = _canonical_app_version(documents_root, errors)
    if candidate_path is not None and not REVISION_PATTERN.fullmatch(str(expected_revision)):
        errors.append("an exact lowercase 40-character expected revision is required with a candidate")
    if candidate_path is not None:
        for relative_path, markers in PENDING_DOCUMENT_MARKERS.items():
            document_path = documents_root / relative_path
            if not document_path.is_file():
                continue
            contents = document_path.read_text(encoding="utf-8")
            for marker in markers:
                if marker.casefold() in contents.casefold():
                    errors.append(f"candidate document retains pending marker in {relative_path}: {marker}")
    candidate = (
        _validate_candidate(
            candidate_path,
            document_hashes,
            canonical_app_version,
            str(expected_revision),
            errors,
        )
        if candidate_path is not None and REVISION_PATTERN.fullmatch(str(expected_revision))
        else None
    )
    candidate_complete = candidate_path is not None and candidate is not None and not errors
    evidence = {
        "schemaVersion": 1,
        "kind": "release-materials-handoff-v1",
        "passed": not errors,
        "foundationQualified": not contract_errors and len(document_hashes) == len(REQUIRED_DOCUMENT_PATHS),
        "contractSha256": hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None,
        "documentSha256": document_hashes,
        "requiredDocumentCount": len(REQUIRED_DOCUMENT_PATHS),
        "artifactPlatformCount": len(ARTIFACT_PLATFORMS),
        "inputDeviceCount": len(INPUT_DEVICE_IDS),
        "screenshotRoleCount": len(SCREENSHOT_ROLES),
        "videoRoleCount": len(VIDEO_ROLES),
        "marketingClaimCount": len(MARKETING_CLAIM_IDS),
        "candidateSupplied": candidate_path is not None,
        "candidateMaterialComplete": candidate_complete,
        "releaseAcceptance": candidate_complete,
        "pendingGates": []
        if candidate_complete
        else [
            "exact-candidate-document-hashes",
            "platform-os-and-size-publication",
            "physical-input-evidence",
            "candidate-screenshots-and-video",
            "evidence-bound-marketing-claims",
            "final-third-party-notice-generation",
            "tested-public-support-route",
        ],
        "errors": errors,
    }
    return errors, evidence


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", type=Path, default=CONTRACT_PATH)
    parser.add_argument("--candidate", type=Path)
    parser.add_argument("--expected-revision")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    errors, evidence = validate_release_materials(
        args.contract.resolve(),
        args.candidate.resolve() if args.candidate is not None else None,
        args.expected_revision,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("Release materials qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.candidate is None:
        print("Release-material foundation qualified; exact candidate materials remain pending.")
    else:
        print("Release materials accepted for the exact candidate record.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
