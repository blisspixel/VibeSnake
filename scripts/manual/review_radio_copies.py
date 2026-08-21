"""Prepare and validate human listening records for exact radio review copies."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, Mapping
from uuid import uuid4


ROOT = Path(__file__).resolve().parents[2]
PUBLIC_ASSET_DIRECTORY = (ROOT / "assets").resolve()
LOCAL_REVIEW_DIRECTORY = (ROOT / "TestResults" / "radio-review").resolve()
MANIFEST_NAME = "review-copy-manifest.json"
MAXIMUM_JSON_BYTES = 4 * 1024 * 1024
MAXIMUM_TRACKS = 128
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
STATION_ID_PATTERN = re.compile(r"[a-z][a-z0-9_]{0,63}")
REVIEWER_ID_PATTERN = re.compile(r"radio-reviewer-[0-9]{3}")
FINDING_ID_PATTERN = re.compile(r"radio-finding-[0-9]{3}")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
DEVICE_IDS = ("headphones", "speakers")
CRITERIA = (
    "complete-playback",
    "no-audible-clipping-or-distortion",
    "clean-start-and-end",
    "relative-level-consistency",
    "station-identity-fit",
    "sustained-listening-comfort",
)
CRITERION_RESULTS = ("pass", "fail", "blocked", "pending")
DECISIONS = (
    "approve-source-replacement",
    "reject-source-replacement",
    "blocked",
    "pending",
)
RECORD_FIELDS = (
    "schemaVersion",
    "kind",
    "stationId",
    "reviewCopyManifestSha256",
    "reviewerId",
    "executedUtc",
    "trackReviews",
    "confirmations",
)
TRACK_REVIEW_FIELDS = (
    "assetId",
    "outputFile",
    "outputSha256",
    "reviewedDeviceIds",
    "criteria",
    "decision",
    "findingIds",
)
CRITERION_FIELDS = ("criterionId", "result")
CONFIRMATION_FIELDS = (
    "listenedToEveryTrackInFull",
    "comparedRelativeLevels",
    "reviewedEveryTrackOnAllDeclaredDevices",
    "understandsNoSourceOrReleaseStateChangesAutomatically",
)


class RadioListeningReviewError(ValueError):
    """Raised when radio listening inputs or records cannot be trusted."""


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError(f"duplicate JSON field: {key}")
        value[key] = item
    return value


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number: {value}")


def _read_json(path: Path, label: str) -> Any:
    if not path.is_file() or path.is_symlink():
        raise RadioListeningReviewError(f"missing regular {label}: {path}")
    try:
        if path.stat().st_size > MAXIMUM_JSON_BYTES:
            raise RadioListeningReviewError(f"{label} exceeds the {MAXIMUM_JSON_BYTES}-byte limit")
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_unique_json_object,
            parse_constant=_reject_json_constant,
        )
    except (OSError, UnicodeError, ValueError) as error:
        raise RadioListeningReviewError(f"unreadable {label}: {path}: {error}") from error


def _strict_keys(value: Any, expected: set[str], label: str, errors: list[str]) -> bool:
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object")
        return False
    if set(value) != expected:
        errors.append(f"{label} fields must be {sorted(expected)!r}; got {sorted(value)!r}")
        return False
    return True


def _valid_utc(value: Any) -> bool:
    if not isinstance(value, str) or UTC_PATTERN.fullmatch(value) is None:
        return False
    try:
        datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        return False
    return True


def require_review_directory(path: Path) -> Path:
    """Accept ignored local review evidence or a workspace outside public source."""
    resolved = path.expanduser().resolve()
    if resolved.is_relative_to(PUBLIC_ASSET_DIRECTORY):
        raise RadioListeningReviewError("radio listening review cannot use the public assets tree")
    if resolved.is_relative_to(ROOT) and not resolved.is_relative_to(LOCAL_REVIEW_DIRECTORY):
        raise RadioListeningReviewError("radio listening review inside the repository must use ignored TestResults")
    return resolved


def require_review_output_path(path: Path, protected_paths: tuple[Path, ...] = ()) -> Path:
    """Keep generated decisions out of public source and away from their own inputs."""
    resolved = path.expanduser().resolve()
    if resolved.suffix.lower() != ".json":
        raise RadioListeningReviewError("radio listening evidence output must use a .json file name")
    if resolved.is_relative_to(ROOT) and not resolved.is_relative_to(LOCAL_REVIEW_DIRECTORY):
        raise RadioListeningReviewError("radio listening evidence inside the repository must use ignored TestResults")
    protected = {item.expanduser().resolve() for item in protected_paths}
    if resolved.name == MANIFEST_NAME or resolved in protected:
        raise RadioListeningReviewError("radio listening evidence output cannot overwrite an input record")
    return resolved


def _write_json_atomic(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    staging = path.parent / f".{path.name}.staging.{uuid4().hex}"
    try:
        with staging.open("x", encoding="utf-8", newline="\n") as handle:
            handle.write(json.dumps(value, indent=2) + "\n")
        os.replace(staging, path)
    except OSError as error:
        raise RadioListeningReviewError(f"could not write radio listening evidence: {error}") from error
    finally:
        if staging.exists():
            staging.unlink()


def validate_review_copies(review_directory: Path) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    """Verify the technical manifest and every exact lossless copy before listening."""
    directory = require_review_directory(review_directory)
    manifest_path = directory / MANIFEST_NAME
    manifest = _read_json(manifest_path, "radio review-copy manifest")
    if not isinstance(manifest, dict):
        raise RadioListeningReviewError("radio review-copy manifest must be an object")
    expected_values = {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-review-copy-set-v1",
        "technicalPass": True,
        "releaseApproved": False,
        "sourceReplacementApproved": False,
        "exportEligibilityChanged": False,
        "humanListeningRequired": True,
        "humanListeningStatus": "pending",
        "sourceBytesModified": False,
        "modifiedSourcePaths": [],
    }
    for field, expected in expected_values.items():
        if manifest.get(field) != expected:
            raise RadioListeningReviewError(f"radio review-copy manifest {field} must be {expected!r}")
    station_id = manifest.get("stationId")
    if not isinstance(station_id, str) or not STATION_ID_PATTERN.fullmatch(station_id):
        raise RadioListeningReviewError("radio review-copy manifest stationId is invalid")
    rows = manifest.get("reviewCopies")
    if not isinstance(rows, list) or not 1 <= len(rows) <= MAXIMUM_TRACKS:
        raise RadioListeningReviewError(f"radio review-copy manifest must contain 1 to {MAXIMUM_TRACKS} tracks")
    summary = manifest.get("summary")
    if not isinstance(summary, dict) or summary.get("trackCount") != len(rows):
        raise RadioListeningReviewError("radio review-copy manifest summary track count is invalid")
    if summary.get("technicalPassCount") != len(rows) or summary.get("technicalFailureCount") != 0:
        raise RadioListeningReviewError("radio review-copy manifest summary does not report a complete technical pass")

    verified: list[dict[str, Any]] = []
    asset_ids: set[str] = set()
    output_names: set[str] = set()
    for index, row in enumerate(rows):
        label = f"radio review-copy manifest reviewCopies[{index}]"
        if not isinstance(row, Mapping):
            raise RadioListeningReviewError(f"{label} must be an object")
        asset_id = row.get("assetId")
        output_name = row.get("outputFile")
        output_sha256 = row.get("outputSha256")
        output_bytes = row.get("outputBytes")
        if not isinstance(asset_id, str) or not asset_id.startswith("asset:audio/radio/") or asset_id in asset_ids:
            raise RadioListeningReviewError(f"{label}.assetId must be a unique radio asset ID")
        if (
            not isinstance(output_name, str)
            or not output_name.endswith(".review.flac")
            or Path(output_name).name != output_name
            or output_name in output_names
        ):
            raise RadioListeningReviewError(f"{label}.outputFile must be a unique review FLAC file name")
        if not isinstance(output_sha256, str) or not SHA256_PATTERN.fullmatch(output_sha256):
            raise RadioListeningReviewError(f"{label}.outputSha256 must be a SHA-256 digest")
        if type(output_bytes) is not int or output_bytes <= 0:
            raise RadioListeningReviewError(f"{label}.outputBytes must be a positive integer")
        if row.get("stationId") != station_id or row.get("technicalPass") is not True or row.get("failures") != []:
            raise RadioListeningReviewError(f"{label} must report the same station and a complete technical pass")
        output_path = directory / output_name
        if not output_path.is_file() or output_path.is_symlink():
            raise RadioListeningReviewError(f"missing regular review copy: {output_path}")
        if output_path.stat().st_size != output_bytes:
            raise RadioListeningReviewError(f"review copy size mismatch: {output_name}")
        if _sha256(output_path) != output_sha256:
            raise RadioListeningReviewError(f"review copy SHA-256 mismatch: {output_name}")
        asset_ids.add(asset_id)
        output_names.add(output_name)
        verified.append(
            {
                "assetId": asset_id,
                "outputFile": output_name,
                "outputSha256": output_sha256,
            }
        )
    verified.sort(key=lambda row: str(row["assetId"]))
    return manifest, verified


def build_template(manifest: Mapping[str, Any], tracks: list[dict[str, Any]], manifest_sha256: str) -> dict[str, Any]:
    """Build an intentionally pending human listening record."""
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-listening-review-v1",
        "stationId": manifest["stationId"],
        "reviewCopyManifestSha256": manifest_sha256,
        "reviewerId": "radio-reviewer-REPLACE",
        "executedUtc": "REPLACE",
        "trackReviews": [
            {
                **track,
                "reviewedDeviceIds": [],
                "criteria": [{"criterionId": criterion, "result": "pending"} for criterion in CRITERIA],
                "decision": "pending",
                "findingIds": [],
            }
            for track in tracks
        ],
        "confirmations": {field: False for field in CONFIRMATION_FIELDS},
    }


def prepare_template(review_directory: Path, output_path: Path) -> dict[str, Any]:
    """Verify copies and exclusively create one pending record template beside them."""
    directory = require_review_directory(review_directory)
    output = output_path.expanduser().resolve()
    if output.parent != directory:
        raise RadioListeningReviewError("listening template must be written directly inside its review directory")
    if output.exists():
        raise RadioListeningReviewError(f"refusing to overwrite existing listening template: {output}")
    manifest, tracks = validate_review_copies(directory)
    template = build_template(manifest, tracks, _sha256(directory / MANIFEST_NAME))
    try:
        with output.open("x", encoding="utf-8", newline="\n") as handle:
            handle.write(json.dumps(template, indent=2) + "\n")
    except OSError as error:
        raise RadioListeningReviewError(f"could not write listening template: {error}") from error
    return template


def verify_review_handoff(review_directory: Path) -> dict[str, Any]:
    """Return a retained technical handoff record without implying human review."""
    directory = require_review_directory(review_directory)
    manifest, tracks = validate_review_copies(directory)
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-listening-handoff-v1",
        "passed": True,
        "technicalInputsVerified": True,
        "stationId": manifest["stationId"],
        "reviewCopyManifestSha256": _sha256(directory / MANIFEST_NAME),
        "trackCount": len(tracks),
        "reviewCopySha256ByAssetId": {track["assetId"]: track["outputSha256"] for track in tracks},
        "humanListeningStatus": "pending",
        "listeningComplete": False,
        "sourceReplacementApproved": False,
        "releaseApproved": False,
        "exportEligibilityChanged": False,
        "pendingGates": ["human-listening-and-source-replacement-approval"],
        "errors": [],
    }


def validate_listening_record(
    review_directory: Path,
    record_path: Path,
) -> tuple[list[str], dict[str, Any]]:
    """Validate a human record against exact copies without changing approval state."""
    errors: list[str] = []
    try:
        directory = require_review_directory(review_directory)
        manifest, tracks = validate_review_copies(directory)
        resolved_record_path = record_path.expanduser().resolve()
        if resolved_record_path.parent != directory:
            raise RadioListeningReviewError("radio listening record must be directly inside its review directory")
        record = _read_json(resolved_record_path, "radio listening record")
    except RadioListeningReviewError as error:
        return [str(error)], {
            "schemaVersion": 1,
            "kind": "vibesnake-radio-listening-decision-v1",
            "passed": False,
            "technicalInputsVerified": False,
            "listeningComplete": False,
            "sourceReplacementApproved": False,
            "releaseApproved": False,
            "exportEligibilityChanged": False,
            "errors": [str(error)],
        }
    if not _strict_keys(record, set(RECORD_FIELDS), "listening record", errors):
        return errors, _decision_evidence(manifest, tracks, False, 0, 0, 0, errors)
    if record["schemaVersion"] != 1:
        errors.append("listening record schemaVersion must be 1")
    if record["kind"] != "vibesnake-radio-listening-review-v1":
        errors.append("listening record kind is invalid")
    if record["stationId"] != manifest["stationId"]:
        errors.append("listening record stationId does not match the review copies")
    manifest_sha256 = _sha256(directory / MANIFEST_NAME)
    if record["reviewCopyManifestSha256"] != manifest_sha256:
        errors.append("listening record manifest SHA-256 does not match the review copies")
    if not REVIEWER_ID_PATTERN.fullmatch(str(record["reviewerId"])):
        errors.append("listening record reviewerId must match radio-reviewer-[0-9]{3}")
    if not _valid_utc(record["executedUtc"]):
        errors.append("listening record executedUtc must use YYYY-MM-DDTHH:MM:SSZ")

    expected_by_asset = {str(track["assetId"]): track for track in tracks}
    reviews = record["trackReviews"]
    review_by_asset: dict[str, Mapping[str, Any]] = {}
    approved_count = 0
    rejected_count = 0
    blocked_count = 0
    all_complete = True
    if not isinstance(reviews, list) or len(reviews) != len(tracks):
        errors.append(f"listening record trackReviews must contain exactly {len(tracks)} rows")
        all_complete = False
    else:
        for index, review in enumerate(reviews):
            label = f"listening record trackReviews[{index}]"
            if not _strict_keys(review, set(TRACK_REVIEW_FIELDS), label, errors):
                all_complete = False
                continue
            asset_id = str(review["assetId"])
            expected = expected_by_asset.get(asset_id)
            if expected is None or asset_id in review_by_asset:
                errors.append(f"{label}.assetId must identify one unique exact review copy")
                all_complete = False
                continue
            review_by_asset[asset_id] = review
            if review["outputFile"] != expected["outputFile"] or review["outputSha256"] != expected["outputSha256"]:
                errors.append(f"{label} output identity does not match the exact review copy")
            devices = review["reviewedDeviceIds"]
            if devices != list(DEVICE_IDS):
                errors.append(f"{label}.reviewedDeviceIds must be {list(DEVICE_IDS)!r} in order")
                all_complete = False
            criterion_rows = review["criteria"]
            results: list[str] = []
            if not isinstance(criterion_rows, list) or len(criterion_rows) != len(CRITERIA):
                errors.append(f"{label}.criteria must contain the exact {len(CRITERIA)} criteria")
                all_complete = False
            else:
                for criterion_index, criterion in enumerate(criterion_rows):
                    criterion_label = f"{label}.criteria[{criterion_index}]"
                    if not _strict_keys(criterion, set(CRITERION_FIELDS), criterion_label, errors):
                        all_complete = False
                        continue
                    if criterion["criterionId"] != CRITERIA[criterion_index]:
                        errors.append(f"{criterion_label}.criterionId is out of contract order")
                    result = criterion["result"]
                    if result not in CRITERION_RESULTS:
                        errors.append(f"{criterion_label}.result is unsupported: {result!r}")
                    else:
                        results.append(str(result))
                        if result == "pending":
                            all_complete = False
            decision = review["decision"]
            if decision not in DECISIONS:
                errors.append(f"{label}.decision is unsupported: {decision!r}")
                all_complete = False
            elif decision == "approve-source-replacement":
                approved_count += 1
                if results != ["pass"] * len(CRITERIA):
                    errors.append(f"{label} cannot approve unless every criterion passes")
            elif decision == "reject-source-replacement":
                rejected_count += 1
                if "fail" not in results:
                    errors.append(f"{label} rejection requires at least one failed criterion")
            elif decision == "blocked":
                blocked_count += 1
                if "blocked" not in results:
                    errors.append(f"{label} blocked decision requires at least one blocked criterion")
            else:
                all_complete = False
            finding_ids = review["findingIds"]
            if (
                not isinstance(finding_ids, list)
                or not all(isinstance(item, str) and FINDING_ID_PATTERN.fullmatch(item) for item in finding_ids)
                or len(finding_ids) != len(set(finding_ids))
            ):
                errors.append(f"{label}.findingIds must contain unique radio-finding-[0-9]{{3}} IDs")
            elif decision in {"reject-source-replacement", "blocked"} and not finding_ids:
                errors.append(f"{label} rejected or blocked decision requires a finding ID")
    if set(review_by_asset) != set(expected_by_asset):
        errors.append("listening record must cover every exact review copy once")
        all_complete = False

    confirmations = record["confirmations"]
    if not _strict_keys(confirmations, set(CONFIRMATION_FIELDS), "listening record confirmations", errors):
        all_complete = False
    else:
        for field in CONFIRMATION_FIELDS:
            if type(confirmations[field]) is not bool:
                errors.append(f"listening record confirmations.{field} must be a boolean")
                all_complete = False
            elif not confirmations[field]:
                all_complete = False
    listening_complete = all_complete and not errors
    return errors, _decision_evidence(
        manifest,
        tracks,
        listening_complete,
        approved_count,
        rejected_count,
        blocked_count,
        errors,
    )


def _decision_evidence(
    manifest: Mapping[str, Any],
    tracks: list[dict[str, Any]],
    listening_complete: bool,
    approved_count: int,
    rejected_count: int,
    blocked_count: int,
    errors: list[str],
) -> dict[str, Any]:
    all_approved = listening_complete and approved_count == len(tracks)
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-listening-decision-v1",
        "passed": not errors,
        "technicalInputsVerified": True,
        "stationId": manifest.get("stationId"),
        "trackCount": len(tracks),
        "approvedTrackCount": approved_count,
        "rejectedTrackCount": rejected_count,
        "blockedTrackCount": blocked_count,
        "listeningComplete": listening_complete,
        "sourceReplacementApproved": all_approved,
        "releaseApproved": False,
        "exportEligibilityChanged": False,
        "pendingGates": [] if all_approved else ["human-listening-and-source-replacement-approval"],
        "errors": list(errors),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("review_directory", type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--prepare-template", type=Path)
    mode.add_argument("--review-record", type=Path)
    mode.add_argument("--verify-inputs", action="store_true")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--require-approved", action="store_true")
    args = parser.parse_args(argv)
    if args.prepare_template is not None:
        if args.output is not None or args.require_approved:
            parser.error("--output and --require-approved apply only to --review-record")
        try:
            template = prepare_template(args.review_directory, args.prepare_template)
        except RadioListeningReviewError as error:
            print(f"Radio listening template preparation failed: {error}", file=sys.stderr)
            return 1
        print(
            f"Radio listening template prepared: station={template['stationId']} "
            f"tracks={len(template['trackReviews'])} listening=pending output={args.prepare_template.resolve()}"
        )
        return 0
    if args.verify_inputs:
        if args.output is None:
            parser.error("--output is required with --verify-inputs")
        if args.require_approved:
            parser.error("--require-approved applies only to --review-record")
        try:
            evidence = verify_review_handoff(args.review_directory)
            output = require_review_output_path(args.output, (args.review_directory / MANIFEST_NAME,))
            _write_json_atomic(output, evidence)
        except RadioListeningReviewError as error:
            print(f"Radio listening handoff verification failed: {error}", file=sys.stderr)
            return 1
        print(
            f"Radio listening handoff verified: station={evidence['stationId']} "
            f"tracks={evidence['trackCount']} listening=pending"
        )
        return 0
    if args.output is None:
        parser.error("--output is required with --review-record")
    errors, evidence = validate_listening_record(args.review_directory, args.review_record)
    try:
        output = require_review_output_path(
            args.output,
            (args.review_directory / MANIFEST_NAME, args.review_record),
        )
        _write_json_atomic(output, evidence)
    except RadioListeningReviewError as error:
        print(f"Radio listening decision output failed: {error}", file=sys.stderr)
        return 1
    if errors:
        print("Radio listening record validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.require_approved and not evidence["sourceReplacementApproved"]:
        print("Radio listening record is valid but does not approve every source replacement.", file=sys.stderr)
        return 1
    print(
        f"Radio listening record valid: station={evidence['stationId']} tracks={evidence['trackCount']} "
        f"complete={str(evidence['listeningComplete']).lower()} "
        f"source_replacement_approved={str(evidence['sourceReplacementApproved']).lower()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
