"""Create hash-bound, non-destructive radio review copies for one station."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping, Sequence
from uuid import uuid4

try:
    from analyze_radio_audio import (
        LOUDNESS_TOLERANCE_LU,
        MAXIMUM_INTERNAL_SILENCE_SECONDS,
        MAXIMUM_LEADING_SILENCE_SECONDS,
        MAXIMUM_TRAILING_SILENCE_SECONDS,
        MAXIMUM_TRUE_PEAK_DBTP,
        MINIMUM_DURATION_SECONDS,
        TARGET_INTEGRATED_LUFS,
        RadioAsset,
        _sha256,
        load_radio_assets,
        parse_ffmpeg_output,
        parse_ffprobe_output,
        parse_silence_output,
    )
except ModuleNotFoundError:
    from scripts.manual.analyze_radio_audio import (
        LOUDNESS_TOLERANCE_LU,
        MAXIMUM_INTERNAL_SILENCE_SECONDS,
        MAXIMUM_LEADING_SILENCE_SECONDS,
        MAXIMUM_TRAILING_SILENCE_SECONDS,
        MAXIMUM_TRUE_PEAK_DBTP,
        MINIMUM_DURATION_SECONDS,
        TARGET_INTEGRATED_LUFS,
        RadioAsset,
        _sha256,
        load_radio_assets,
        parse_ffmpeg_output,
        parse_ffprobe_output,
        parse_silence_output,
    )


PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INVENTORY = PROJECT_ROOT / "config" / "content_inventory.json"
DEFAULT_CURATION = PROJECT_ROOT / "config" / "content_curation_v1.json"
DEFAULT_ANALYSIS = PROJECT_ROOT / "TestResults" / "radio-audio" / "radio_audio_qualification.json"
DEFAULT_OUTPUT_ROOT = PROJECT_ROOT / "TestResults" / "radio-review"
ALLOWED_LOCAL_OUTPUT_ROOTS = (PROJECT_ROOT / "TestResults", PROJECT_ROOT / "archive")
MAXIMUM_JSON_BYTES = 16 * 1024 * 1024
MAXIMUM_WORKERS = 4
EDGE_SILENCE_RETAIN_SECONDS = 0.25
EDGE_CORRECTION_MARGIN_SECONDS = 0.1
MAXIMUM_NORMALIZATION_ATTEMPTS = 3
NORMALIZATION_LRA_TARGET_LU = 50.0
OUTPUT_CODEC = "flac"
OUTPUT_SAMPLE_FORMAT = "s16"
_STATION_ID_PATTERN = re.compile(r"[a-z0-9]+(?:_[a-z0-9]+)*")
_LOUDNORM_FIELDS = {
    "inputIntegratedLufs": "input_i",
    "inputTruePeakDbtp": "input_tp",
    "inputLoudnessRangeLu": "input_lra",
    "inputThresholdLufs": "input_thresh",
    "outputIntegratedLufs": "output_i",
    "outputTruePeakDbtp": "output_tp",
    "outputLoudnessRangeLu": "output_lra",
    "outputThresholdLufs": "output_thresh",
    "targetOffsetDb": "target_offset",
}


class RadioReviewCopyError(ValueError):
    """Raised when a source or generated review set cannot be trusted."""


def _finite_number(value: Any, label: str) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError) as error:
        raise RadioReviewCopyError(f"{label} is not numeric") from error
    if not math.isfinite(number):
        raise RadioReviewCopyError(f"{label} is not finite")
    return number


def _load_json(path: Path) -> dict[str, Any]:
    try:
        if path.stat().st_size > MAXIMUM_JSON_BYTES:
            raise RadioReviewCopyError(f"JSON input exceeds {MAXIMUM_JSON_BYTES} bytes: {path.name}")
        value = json.loads(path.read_text(encoding="utf-8"))
    except RadioReviewCopyError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RadioReviewCopyError(f"JSON input is unreadable: {path.name}: {error}") from error
    if not isinstance(value, dict):
        raise RadioReviewCopyError(f"JSON input must contain an object: {path.name}")
    return value


def _run(command: Sequence[str], timeout_seconds: int) -> subprocess.CompletedProcess[str]:
    try:
        result = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise RadioReviewCopyError(f"tool execution failed: {command[0]}: {error}") from error
    if result.returncode != 0:
        diagnostic = (result.stderr or result.stdout).strip()[-1000:]
        raise RadioReviewCopyError(f"tool failed with exit code {result.returncode}: {command[0]}: {diagnostic}")
    return result


def _tool_version(command: str) -> str:
    result = _run((command, "-version"), 30)
    lines = (result.stdout or result.stderr).splitlines()
    if not lines:
        raise RadioReviewCopyError(f"tool version check returned no output: {command}")
    return lines[0].strip()


def parse_loudnorm_output(output: str) -> dict[str, Any]:
    """Extract the one complete JSON loudnorm summary from FFmpeg diagnostics."""
    decoder = json.JSONDecoder()
    candidates: list[Mapping[str, Any]] = []
    for match in re.finditer(r"\{", output):
        try:
            value, _ = decoder.raw_decode(output[match.start() :])
        except json.JSONDecodeError:
            continue
        if isinstance(value, Mapping) and set(_LOUDNORM_FIELDS.values()).issubset(value):
            candidates.append(value)
    if len(candidates) != 1:
        raise RadioReviewCopyError("ffmpeg must emit exactly one complete loudnorm JSON summary")
    raw = candidates[0]
    result = {
        output_field: round(_finite_number(raw[input_field], output_field), 2)
        for output_field, input_field in _LOUDNORM_FIELDS.items()
    }
    normalization_type = raw.get("normalization_type")
    if not isinstance(normalization_type, str) or normalization_type.lower() not in {"linear", "dynamic"}:
        raise RadioReviewCopyError("ffmpeg emitted an invalid loudnorm normalization type")
    result["normalizationType"] = normalization_type.lower()
    return result


def compute_trim_plan(track: Mapping[str, Any]) -> dict[str, float]:
    """Retain a short edge pad while removing measured leading and trailing excess."""
    duration = _finite_number(track.get("durationSeconds"), "duration")
    leading = _finite_number(track.get("leadingSilenceSeconds"), "leading silence")
    trailing = _finite_number(track.get("trailingSilenceSeconds"), "trailing silence")
    if duration <= 0.0 or leading < 0.0 or trailing < 0.0 or leading + trailing >= duration:
        raise RadioReviewCopyError("analysis contains invalid edge-silence bounds")
    start = max(0.0, leading - EDGE_SILENCE_RETAIN_SECONDS)
    end = duration - max(0.0, trailing - EDGE_SILENCE_RETAIN_SECONDS)
    output_duration = end - start
    if output_duration < MINIMUM_DURATION_SECONDS:
        raise RadioReviewCopyError("edge trimming would produce an undersized review copy")
    return {
        "sourceDurationSeconds": round(duration, 6),
        "startSeconds": round(start, 6),
        "endSeconds": round(end, 6),
        "expectedOutputDurationSeconds": round(output_duration, 6),
        "retainedEdgeSilenceSeconds": EDGE_SILENCE_RETAIN_SECONDS,
    }


def _number(value: float) -> str:
    return f"{value:.6f}".rstrip("0").rstrip(".")


def build_trim_filter(trim: Mapping[str, float]) -> str:
    return f"atrim=start={_number(trim['startSeconds'])}:end={_number(trim['endSeconds'])},asetpts=PTS-STARTPTS"


def build_second_pass_filter(trim_filter: str, first_pass: Mapping[str, Any], sample_rate_hz: int) -> str:
    """Build a closed second-pass filter from FFmpeg's first-pass measurements."""
    parameters = {
        "I": TARGET_INTEGRATED_LUFS,
        "TP": MAXIMUM_TRUE_PEAK_DBTP,
        "LRA": NORMALIZATION_LRA_TARGET_LU,
        "measured_I": first_pass["inputIntegratedLufs"],
        "measured_LRA": first_pass["inputLoudnessRangeLu"],
        "measured_TP": first_pass["inputTruePeakDbtp"],
        "measured_thresh": first_pass["inputThresholdLufs"],
        "offset": first_pass["targetOffsetDb"],
    }
    loudnorm = ":".join(f"{key}={_number(_finite_number(value, key))}" for key, value in parameters.items())
    return f"{trim_filter},loudnorm={loudnorm}:linear=true:print_format=json,aresample={sample_rate_hz}"


def compute_edge_correction(silence: Mapping[str, Any]) -> dict[str, float]:
    """Compute the smallest bounded correction that clears the post-normalization ceiling."""
    leading = _finite_number(silence.get("leadingSilenceSeconds"), "measured leading silence")
    trailing = _finite_number(silence.get("trailingSilenceSeconds"), "measured trailing silence")
    return {
        "additionalStartSeconds": round(
            max(0.0, leading - MAXIMUM_LEADING_SILENCE_SECONDS + EDGE_CORRECTION_MARGIN_SECONDS),
            6,
        ),
        "additionalEndSeconds": round(
            max(0.0, trailing - MAXIMUM_TRAILING_SILENCE_SECONDS + EDGE_CORRECTION_MARGIN_SECONDS),
            6,
        ),
    }


def validate_review_measurement(
    media: Mapping[str, Any],
    measurement: Mapping[str, Any],
    silence: Mapping[str, Any],
    source_channels: int,
    source_sample_rate_hz: int,
    expected_duration_seconds: float,
) -> list[str]:
    """Apply the provisional technical policy without making an approval decision."""
    failures: list[str] = []
    if media.get("codec") != OUTPUT_CODEC:
        failures.append("review copy codec is not FLAC")
    if media.get("channels") != source_channels:
        failures.append("review copy channel count differs from source")
    if media.get("sampleRateHz") != source_sample_rate_hz:
        failures.append("review copy sample rate differs from source")
    if abs(float(media.get("durationSeconds", 0.0)) - expected_duration_seconds) > 0.1:
        failures.append("review copy duration differs from the trim plan")
    minimum_loudness = TARGET_INTEGRATED_LUFS - LOUDNESS_TOLERANCE_LU
    maximum_loudness = TARGET_INTEGRATED_LUFS + LOUDNESS_TOLERANCE_LU
    if not minimum_loudness <= float(measurement.get("integratedLufs", math.inf)) <= maximum_loudness:
        failures.append("review copy integrated loudness is outside the admission band")
    if float(measurement.get("truePeakDbtp", math.inf)) > MAXIMUM_TRUE_PEAK_DBTP:
        failures.append("review copy true peak exceeds the admission ceiling")
    if float(silence.get("leadingSilenceSeconds", math.inf)) > MAXIMUM_LEADING_SILENCE_SECONDS:
        failures.append("review copy leading silence exceeds the admission ceiling")
    if float(silence.get("trailingSilenceSeconds", math.inf)) > MAXIMUM_TRAILING_SILENCE_SECONDS:
        failures.append("review copy trailing silence exceeds the admission ceiling")
    if float(silence.get("maximumInternalSilenceSeconds", math.inf)) > MAXIMUM_INTERNAL_SILENCE_SECONDS:
        failures.append("review copy internal silence exceeds the admission ceiling")
    return failures


def _measure_output(
    path: Path,
    ffmpeg: str,
    ffprobe: str,
    timeout_seconds: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    probe = _run(
        (
            ffprobe,
            "-v",
            "error",
            "-select_streams",
            "a",
            "-show_entries",
            "format=duration,bit_rate,format_name:stream=codec_name,sample_rate,channels,channel_layout,duration,bit_rate",
            "-of",
            "json",
            os.fspath(path),
        ),
        timeout_seconds,
    )
    media = parse_ffprobe_output(probe.stdout)
    decode = _run(
        (
            ffmpeg,
            "-nostdin",
            "-hide_banner",
            "-nostats",
            "-loglevel",
            "info",
            "-i",
            os.fspath(path),
            "-map",
            "0:a:0",
            "-af",
            "ebur128=peak=true:framelog=verbose,volumedetect,silencedetect=noise=-60dB:d=1",
            "-f",
            "null",
            "-",
        ),
        timeout_seconds,
    )
    return media, parse_ffmpeg_output(decode.stderr), parse_silence_output(decode.stderr, media["durationSeconds"])


def _prepare_track(
    asset: RadioAsset,
    analysis: Mapping[str, Any],
    staging_directory: Path,
    ffmpeg: str,
    ffprobe: str,
    timeout_seconds: int,
) -> dict[str, Any]:
    if _sha256(asset.source_path) != analysis.get("sourceSha256"):
        raise RadioReviewCopyError(f"source no longer matches analysis evidence: {asset.relative_path}")
    trim = compute_trim_plan(analysis)
    source_sample_rate = int(analysis["sampleRateHz"])
    output_name = f"{Path(asset.relative_path).stem}.review.flac"
    output_path = staging_directory / output_name
    edge_trim_adjustments: list[dict[str, Any]] = []
    for attempt in range(1, MAXIMUM_NORMALIZATION_ATTEMPTS + 1):
        trim_filter = build_trim_filter(trim)
        first_pass_command = (
            ffmpeg,
            "-nostdin",
            "-hide_banner",
            "-nostats",
            "-loglevel",
            "info",
            "-i",
            os.fspath(asset.source_path),
            "-map",
            "0:a:0",
            "-af",
            (
                f"{trim_filter},loudnorm=I={_number(TARGET_INTEGRATED_LUFS)}:"
                f"TP={_number(MAXIMUM_TRUE_PEAK_DBTP)}:LRA={_number(NORMALIZATION_LRA_TARGET_LU)}:"
                "print_format=json"
            ),
            "-f",
            "null",
            "-",
        )
        first_pass = parse_loudnorm_output(_run(first_pass_command, timeout_seconds).stderr)
        second_pass_filter = build_second_pass_filter(trim_filter, first_pass, source_sample_rate)
        if output_path.exists():
            output_path.unlink()
        second_pass_command = (
            ffmpeg,
            "-nostdin",
            "-hide_banner",
            "-nostats",
            "-loglevel",
            "info",
            "-fflags",
            "+bitexact",
            "-i",
            os.fspath(asset.source_path),
            "-map",
            "0:a:0",
            "-map_metadata",
            "-1",
            "-map_chapters",
            "-1",
            "-vn",
            "-af",
            second_pass_filter,
            "-sample_fmt",
            OUTPUT_SAMPLE_FORMAT,
            "-c:a",
            OUTPUT_CODEC,
            "-compression_level",
            "8",
            "-flags:a",
            "+bitexact",
            "-threads",
            "1",
            os.fspath(output_path),
        )
        second_pass = parse_loudnorm_output(_run(second_pass_command, timeout_seconds).stderr)
        if not output_path.is_file() or output_path.is_symlink():
            raise RadioReviewCopyError(f"ffmpeg did not create a regular review copy: {output_name}")
        media, measurement, silence = _measure_output(output_path, ffmpeg, ffprobe, timeout_seconds)
        failures = validate_review_measurement(
            media,
            measurement,
            silence,
            int(analysis["channels"]),
            source_sample_rate,
            trim["expectedOutputDurationSeconds"],
        )
        correction = compute_edge_correction(silence)
        non_edge_failures = [failure for failure in failures if "silence exceeds" not in failure]
        correction_required = any(correction.values()) and not non_edge_failures
        if not correction_required or attempt == MAXIMUM_NORMALIZATION_ATTEMPTS:
            break
        next_start = trim["startSeconds"] + correction["additionalStartSeconds"]
        next_end = trim["endSeconds"] - correction["additionalEndSeconds"]
        next_duration = next_end - next_start
        if next_duration < MINIMUM_DURATION_SECONDS:
            failures.append("post-normalization edge correction would produce an undersized review copy")
            break
        edge_trim_adjustments.append(
            {
                "attempt": attempt,
                "measuredLeadingSilenceSeconds": silence["leadingSilenceSeconds"],
                "measuredTrailingSilenceSeconds": silence["trailingSilenceSeconds"],
                **correction,
            }
        )
        trim = {
            **trim,
            "startSeconds": round(next_start, 6),
            "endSeconds": round(next_end, 6),
            "expectedOutputDurationSeconds": round(next_duration, 6),
        }
    if _sha256(asset.source_path) != analysis["sourceSha256"]:
        failures.append("source changed while preparing the review copy")
    return {
        "assetId": asset.asset_id,
        "stationId": asset.station_id,
        "sourcePath": asset.relative_path,
        "sourceBytes": asset.expected_bytes,
        "sourceSha256": asset.expected_sha256,
        "outputFile": output_name,
        "outputBytes": output_path.stat().st_size,
        "outputSha256": _sha256(output_path),
        "trim": trim,
        "normalizationAttemptCount": attempt,
        "postNormalizationEdgeTrimAdjustments": edge_trim_adjustments,
        "firstPass": first_pass,
        "secondPass": second_pass,
        "media": media,
        "measurement": measurement,
        "silence": silence,
        "technicalPass": not failures,
        "failures": failures,
    }


def _require_output_root(output_root: Path) -> Path:
    resolved = output_root.expanduser().resolve()
    if resolved.is_relative_to(PROJECT_ROOT) and not any(
        resolved.is_relative_to(root.resolve()) for root in ALLOWED_LOCAL_OUTPUT_ROOTS
    ):
        raise RadioReviewCopyError("output inside the repository must stay under TestResults or archive")
    return resolved


def validate_replace_target(path: Path, station_id: str) -> None:
    """Permit replacement only for a review set previously owned by this tool."""
    if path.is_symlink() or not path.is_dir():
        raise RadioReviewCopyError(f"refusing to replace a non-directory review set: {path}")
    manifest_path = path / "review-copy-manifest.json"
    if manifest_path.is_symlink() or not manifest_path.is_file():
        raise RadioReviewCopyError(f"refusing to replace a review set without its manifest: {path}")
    manifest = _load_json(manifest_path)
    if (
        manifest.get("kind") != "vibesnake-radio-review-copy-set-v1"
        or manifest.get("schemaVersion") != 1
        or manifest.get("stationId") != station_id
    ):
        raise RadioReviewCopyError(f"refusing to replace a review set with a foreign manifest: {path}")


def _write_json(path: Path, value: Mapping[str, Any]) -> None:
    payload = json.dumps(value, indent=2, sort_keys=True) + "\n"
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(payload)
        handle.flush()
        os.fsync(handle.fileno())


def _source_set_sha256(assets: Sequence[RadioAsset]) -> str:
    payload = json.dumps(
        [{"path": asset.relative_path, "sha256": asset.expected_sha256} for asset in assets],
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def prepare_station(
    repository_root: Path,
    inventory_path: Path,
    curation_path: Path,
    analysis_path: Path,
    output_root: Path,
    station_id: str,
    ffmpeg: str,
    ffprobe: str,
    workers: int,
    timeout_seconds: int,
    replace: bool,
) -> tuple[Path, dict[str, Any]]:
    """Prepare one complete station atomically and retain technical evidence."""
    assets, input_identity = load_radio_assets(repository_root, inventory_path, curation_path)
    selected_assets = [asset for asset in assets if asset.station_id == station_id]
    if not 11 <= len(selected_assets) <= 13:
        raise RadioReviewCopyError(f"station must contain 11 through 13 tracks: {station_id}")
    analysis_evidence = _load_json(analysis_path)
    if analysis_evidence.get("kind") != "vibesnake-radio-audio-qualification-v1":
        raise RadioReviewCopyError("radio analysis evidence identity is unsupported")
    if analysis_evidence.get("sourceBytesModified") is not False:
        raise RadioReviewCopyError("radio analysis evidence reports modified source bytes")
    if analysis_evidence.get("inputs") != input_identity:
        raise RadioReviewCopyError("radio analysis evidence does not match current inventory and curation")
    tracks = analysis_evidence.get("tracks")
    decoder_errors = analysis_evidence.get("decoderErrors")
    if not isinstance(tracks, list) or len(tracks) != len(assets) or decoder_errors != []:
        raise RadioReviewCopyError("radio analysis must contain all tracks with zero decoder errors")
    analysis_by_path = {str(track.get("path")): track for track in tracks if isinstance(track, Mapping)}
    if set(analysis_by_path) != {asset.relative_path for asset in assets}:
        raise RadioReviewCopyError("radio analysis track set does not match the inventory")
    source_hashes_before = {asset.relative_path: _sha256(asset.source_path) for asset in assets}
    for asset in assets:
        if source_hashes_before[asset.relative_path] != asset.expected_sha256:
            raise RadioReviewCopyError(f"source does not match inventory: {asset.relative_path}")
        if analysis_by_path[asset.relative_path].get("sourceSha256") != asset.expected_sha256:
            raise RadioReviewCopyError(f"analysis does not match inventory: {asset.relative_path}")

    resolved_output_root = _require_output_root(output_root)
    resolved_output_root.mkdir(parents=True, exist_ok=True)
    final_directory = resolved_output_root / station_id
    staging_directory = resolved_output_root / f".{station_id}.staging.{uuid4().hex}"
    if final_directory.exists() and not replace:
        raise RadioReviewCopyError(f"review set already exists; pass --replace to replace it: {final_directory}")
    if final_directory.exists():
        validate_replace_target(final_directory, station_id)
    output_names = [f"{Path(asset.relative_path).stem}.review.flac" for asset in selected_assets]
    if len(output_names) != len(set(output_names)):
        raise RadioReviewCopyError("station review-copy file names collide")
    staging_directory.mkdir()
    results: list[dict[str, Any]] = []
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
            futures = {
                executor.submit(
                    _prepare_track,
                    asset,
                    analysis_by_path[asset.relative_path],
                    staging_directory,
                    ffmpeg,
                    ffprobe,
                    timeout_seconds,
                ): asset
                for asset in selected_assets
            }
            for completed, future in enumerate(concurrent.futures.as_completed(futures), start=1):
                asset = futures[future]
                try:
                    result = future.result()
                except RadioReviewCopyError as error:
                    raise RadioReviewCopyError(f"review copy failed for {asset.relative_path}: {error}") from error
                results.append(result)
                print(f"Prepared {completed}/{len(selected_assets)}: {asset.relative_path}", file=sys.stderr)
        results.sort(key=lambda item: str(item["sourcePath"]))
        modified_sources = sorted(
            asset.relative_path
            for asset in assets
            if _sha256(asset.source_path) != source_hashes_before[asset.relative_path]
        )
        technical_pass = all(bool(result["technicalPass"]) for result in results) and not modified_sources
        manifest = {
            "schemaVersion": 1,
            "kind": "vibesnake-radio-review-copy-set-v1",
            "createdUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "stationId": station_id,
            "technicalPass": technical_pass,
            "releaseApproved": False,
            "sourceReplacementApproved": False,
            "exportEligibilityChanged": False,
            "humanListeningRequired": True,
            "humanListeningStatus": "pending",
            "sourceBytesModified": bool(modified_sources),
            "modifiedSourcePaths": modified_sources,
            "toolchain": {
                "ffmpeg": _tool_version(ffmpeg),
                "ffprobe": _tool_version(ffprobe),
                "workers": workers,
                "perTrackTimeoutSeconds": timeout_seconds,
            },
            "policy": {
                "targetIntegratedLufs": TARGET_INTEGRATED_LUFS,
                "loudnessToleranceLu": LOUDNESS_TOLERANCE_LU,
                "maximumTruePeakDbtp": MAXIMUM_TRUE_PEAK_DBTP,
                "normalizationLraTargetLu": NORMALIZATION_LRA_TARGET_LU,
                "maximumLeadingSilenceSeconds": MAXIMUM_LEADING_SILENCE_SECONDS,
                "maximumTrailingSilenceSeconds": MAXIMUM_TRAILING_SILENCE_SECONDS,
                "maximumInternalSilenceSeconds": MAXIMUM_INTERNAL_SILENCE_SECONDS,
                "retainedEdgeSilenceSeconds": EDGE_SILENCE_RETAIN_SECONDS,
                "postNormalizationEdgeCorrectionMarginSeconds": EDGE_CORRECTION_MARGIN_SECONDS,
                "maximumNormalizationAttempts": MAXIMUM_NORMALIZATION_ATTEMPTS,
                "outputCodec": OUTPUT_CODEC,
                "outputSampleFormat": OUTPUT_SAMPLE_FORMAT,
            },
            "inputs": {
                **input_identity,
                "analysisEvidenceSha256": _sha256(analysis_path),
                "radioSourceSetSha256": _source_set_sha256(assets),
            },
            "summary": {
                "trackCount": len(results),
                "technicalPassCount": sum(bool(result["technicalPass"]) for result in results),
                "technicalFailureCount": sum(not bool(result["technicalPass"]) for result in results),
                "sourceBytes": sum(int(result["sourceBytes"]) for result in results),
                "outputBytes": sum(int(result["outputBytes"]) for result in results),
                "linearNormalizationCount": sum(
                    result["secondPass"]["normalizationType"] == "linear" for result in results
                ),
                "dynamicNormalizationCount": sum(
                    result["secondPass"]["normalizationType"] == "dynamic" for result in results
                ),
            },
            "reviewCopies": results,
        }
        _write_json(staging_directory / "review-copy-manifest.json", manifest)
        backup_directory: Path | None = None
        if final_directory.exists():
            backup_directory = resolved_output_root / f".{station_id}.backup.{uuid4().hex}"
            os.replace(final_directory, backup_directory)
        try:
            os.replace(staging_directory, final_directory)
        except OSError:
            if backup_directory is not None and backup_directory.exists() and not final_directory.exists():
                os.replace(backup_directory, final_directory)
            raise
        if backup_directory is not None:
            shutil.rmtree(backup_directory)
        return final_directory, manifest
    finally:
        if staging_directory.exists():
            shutil.rmtree(staging_directory)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--station", required=True)
    parser.add_argument("--repository-root", type=Path, default=PROJECT_ROOT)
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY)
    parser.add_argument("--curation", type=Path, default=DEFAULT_CURATION)
    parser.add_argument("--analysis", type=Path, default=DEFAULT_ANALYSIS)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    parser.add_argument("--workers", type=int, default=min(2, os.cpu_count() or 1))
    parser.add_argument("--timeout-seconds", type=int, default=600)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    try:
        if not _STATION_ID_PATTERN.fullmatch(args.station):
            raise RadioReviewCopyError("station must be a lowercase underscore identifier")
        if not 1 <= args.workers <= MAXIMUM_WORKERS:
            raise RadioReviewCopyError(f"workers must be between 1 and {MAXIMUM_WORKERS}")
        if not 30 <= args.timeout_seconds <= 1800:
            raise RadioReviewCopyError("timeout-seconds must be between 30 and 1800")
        ffmpeg = shutil.which(args.ffmpeg)
        ffprobe = shutil.which(args.ffprobe)
        if ffmpeg is None or ffprobe is None:
            raise RadioReviewCopyError("ffmpeg and ffprobe must both be available")
        output_directory, manifest = prepare_station(
            args.repository_root,
            args.inventory,
            args.curation,
            args.analysis,
            args.output_root,
            args.station,
            ffmpeg,
            ffprobe,
            args.workers,
            args.timeout_seconds,
            args.replace,
        )
    except (RadioReviewCopyError, OSError) as error:
        print(f"Radio review-copy preparation failed: {error}", file=sys.stderr)
        return 2
    summary = manifest["summary"]
    print(
        f"Radio review copies: station={args.station} tracks={summary['trackCount']} "
        f"technical_pass={summary['technicalPassCount']} failures={summary['technicalFailureCount']} "
        f"output={output_directory}"
    )
    return 0 if manifest["technicalPass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
