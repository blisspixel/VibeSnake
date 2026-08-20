"""Fully decode and measure the public radio library without modifying or approving it."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import math
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping, Sequence


PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INVENTORY = PROJECT_ROOT / "config" / "content_inventory.json"
DEFAULT_CURATION = PROJECT_ROOT / "config" / "content_curation_v1.json"
DEFAULT_OUTPUT = PROJECT_ROOT / "TestResults" / "radio-audio" / "radio_audio_qualification.json"
ALLOWED_LOCAL_OUTPUT_ROOTS = (PROJECT_ROOT / "TestResults", PROJECT_ROOT / "archive")
MAXIMUM_JSON_BYTES = 8 * 1024 * 1024
MAXIMUM_WORKERS = 8
MINIMUM_DURATION_SECONDS = 30.0
MAXIMUM_DURATION_SECONDS = 15.0 * 60.0
MINIMUM_SAMPLE_RATE_HZ = 44_100
MAXIMUM_SAMPLE_RATE_HZ = 192_000
TARGET_INTEGRATED_LUFS = -18.0
LOUDNESS_TOLERANCE_LU = 2.0
MAXIMUM_TRUE_PEAK_DBTP = -1.0
SILENCE_NOISE_DBFS = -60.0
MINIMUM_REPORTED_SILENCE_SECONDS = 1.0
MAXIMUM_LEADING_SILENCE_SECONDS = 2.0
MAXIMUM_TRAILING_SILENCE_SECONDS = 2.0
MAXIMUM_INTERNAL_SILENCE_SECONDS = 5.0

_EBUR128_PATTERN = re.compile(
    r"Integrated loudness:\s+I:\s+(?P<integrated>-?(?:inf|\d+(?:\.\d+)?))\s+LUFS"
    r".*?Loudness range:\s+LRA:\s+(?P<range>-?(?:inf|\d+(?:\.\d+)?))\s+LU"
    r".*?True peak:\s+Peak:\s+(?P<peak>-?(?:inf|\d+(?:\.\d+)?))\s+dBFS",
    re.DOTALL | re.IGNORECASE,
)
_VOLUME_PATTERNS = {
    "decodedSampleCount": re.compile(r"\bn_samples:\s+(\d+)\s*$", re.MULTILINE),
    "meanVolumeDbfs": re.compile(r"\bmean_volume:\s+(-?(?:inf|\d+(?:\.\d+)?))\s+dB\s*$", re.MULTILINE),
    "samplePeakDbfs": re.compile(r"\bmax_volume:\s+(-?(?:inf|\d+(?:\.\d+)?))\s+dB\s*$", re.MULTILINE),
    "highestBucketSampleCount": re.compile(r"\bhistogram_0db:\s+(\d+)\s*$", re.MULTILINE),
}
_SILENCE_EVENT_PATTERN = re.compile(
    r"silence_(?P<event>start|end):\s+(?P<time>\d+(?:\.\d+)?)"
    r"(?:\s+\|\s+silence_duration:\s+(?P<duration>\d+(?:\.\d+)?))?"
)


class RadioAudioAnalysisError(ValueError):
    """Raised when source or tool output cannot support a bounded qualification."""


@dataclass(frozen=True)
class RadioAsset:
    asset_id: str
    station_id: str
    relative_path: str
    expected_bytes: int
    expected_sha256: str
    source_path: Path


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _load_json(path: Path) -> dict[str, Any]:
    try:
        size = path.stat().st_size
        if size > MAXIMUM_JSON_BYTES:
            raise RadioAudioAnalysisError(f"JSON input exceeds {MAXIMUM_JSON_BYTES} bytes: {path.name}")
        value = json.loads(path.read_text(encoding="utf-8"))
    except RadioAudioAnalysisError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RadioAudioAnalysisError(f"JSON input is unreadable: {path.name}: {error}") from error
    if not isinstance(value, dict):
        raise RadioAudioAnalysisError(f"JSON input must contain an object: {path.name}")
    return value


def _finite_number(value: Any, label: str) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError) as error:
        raise RadioAudioAnalysisError(f"{label} is not numeric") from error
    if not math.isfinite(number):
        raise RadioAudioAnalysisError(f"{label} is not finite")
    return number


def _parse_measurement_number(value: str, label: str) -> float:
    if value.lower() in {"inf", "-inf"}:
        raise RadioAudioAnalysisError(f"{label} is not finite")
    return _finite_number(value, label)


def parse_ffprobe_output(output: str) -> dict[str, Any]:
    """Parse the closed audio fields emitted by the qualification probe."""
    try:
        value = json.loads(output)
    except json.JSONDecodeError as error:
        raise RadioAudioAnalysisError(f"ffprobe did not emit valid JSON: {error}") from error
    streams = value.get("streams") if isinstance(value, dict) else None
    format_value = value.get("format") if isinstance(value, dict) else None
    if not isinstance(streams, list) or len(streams) != 1 or not isinstance(streams[0], Mapping):
        raise RadioAudioAnalysisError("ffprobe must report exactly one audio stream")
    if not isinstance(format_value, Mapping):
        raise RadioAudioAnalysisError("ffprobe must report container metadata")
    stream = streams[0]
    codec = stream.get("codec_name")
    layout = stream.get("channel_layout", "unknown")
    channels = stream.get("channels")
    if not isinstance(codec, str) or not codec:
        raise RadioAudioAnalysisError("ffprobe omitted the audio codec")
    if not isinstance(layout, str) or not layout:
        raise RadioAudioAnalysisError("ffprobe emitted an invalid channel layout")
    if not isinstance(channels, int) or channels <= 0:
        raise RadioAudioAnalysisError("ffprobe emitted an invalid channel count")
    sample_rate = int(_finite_number(stream.get("sample_rate"), "sample rate"))
    duration = _finite_number(format_value.get("duration", stream.get("duration")), "duration")
    bit_rate = int(_finite_number(format_value.get("bit_rate", stream.get("bit_rate")), "bit rate"))
    format_name = format_value.get("format_name")
    if not isinstance(format_name, str) or not format_name:
        raise RadioAudioAnalysisError("ffprobe omitted the container format")
    return {
        "codec": codec,
        "container": format_name,
        "sampleRateHz": sample_rate,
        "channels": channels,
        "channelLayout": layout,
        "durationSeconds": round(duration, 6),
        "bitRateBps": bit_rate,
    }


def parse_ffmpeg_output(output: str) -> dict[str, Any]:
    """Parse final EBU R 128 and decoded-volume summaries from FFmpeg output."""
    matches = list(_EBUR128_PATTERN.finditer(output))
    if len(matches) != 1:
        raise RadioAudioAnalysisError("ffmpeg must emit exactly one EBU R 128 summary")
    match = matches[0]
    result: dict[str, Any] = {
        "integratedLufs": _parse_measurement_number(match.group("integrated"), "integrated loudness"),
        "loudnessRangeLu": _parse_measurement_number(match.group("range"), "loudness range"),
        "truePeakDbtp": _parse_measurement_number(match.group("peak"), "true peak"),
    }
    for field, pattern in _VOLUME_PATTERNS.items():
        values = pattern.findall(output)
        if not values:
            if field == "highestBucketSampleCount":
                result[field] = 0
                continue
            raise RadioAudioAnalysisError(f"ffmpeg omitted {field}")
        raw = values[-1]
        result[field] = int(raw) if field.endswith("Count") else _parse_measurement_number(raw, field)
    for field in ("integratedLufs", "loudnessRangeLu", "truePeakDbtp", "meanVolumeDbfs", "samplePeakDbfs"):
        result[field] = round(result[field], 1)
    return result


def parse_silence_output(output: str, duration_seconds: float) -> dict[str, Any]:
    """Reduce ordered FFmpeg silence events to bounded review measurements."""
    duration_seconds = _finite_number(duration_seconds, "track duration")
    open_start: float | None = None
    intervals: list[tuple[float, float, float]] = []
    for match in _SILENCE_EVENT_PATTERN.finditer(output):
        event = match.group("event")
        event_time = _parse_measurement_number(match.group("time"), f"silence {event}")
        if not 0.0 <= event_time <= duration_seconds + 0.1:
            raise RadioAudioAnalysisError("silence event is outside the track duration")
        if event == "start":
            if open_start is not None:
                raise RadioAudioAnalysisError("ffmpeg emitted nested silence intervals")
            open_start = event_time
            continue
        if open_start is None:
            raise RadioAudioAnalysisError("ffmpeg emitted a silence end without a start")
        reported_duration = match.group("duration")
        interval_duration = event_time - open_start
        if reported_duration is not None and abs(float(reported_duration) - interval_duration) > 0.02:
            raise RadioAudioAnalysisError("ffmpeg silence duration disagrees with its interval")
        intervals.append((open_start, event_time, interval_duration))
        open_start = None
    if open_start is not None:
        intervals.append((open_start, duration_seconds, duration_seconds - open_start))
    leading = intervals[0][2] if intervals and intervals[0][0] <= 0.05 else 0.0
    trailing = intervals[-1][2] if intervals and intervals[-1][1] >= duration_seconds - 0.1 else 0.0
    internal = [
        interval[2]
        for index, interval in enumerate(intervals)
        if not (index == 0 and leading > 0.0) and not (index == len(intervals) - 1 and trailing > 0.0)
    ]
    return {
        "silenceIntervalCount": len(intervals),
        "totalSilenceSeconds": round(sum(interval[2] for interval in intervals), 6),
        "leadingSilenceSeconds": round(leading, 6),
        "trailingSilenceSeconds": round(trailing, 6),
        "maximumInternalSilenceSeconds": round(max(internal, default=0.0), 6),
    }


def _run(command: Sequence[str], timeout_seconds: int) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise RadioAudioAnalysisError(f"tool execution failed: {command[0]}: {error}") from error


def _tool_version(command: str) -> str:
    result = _run((command, "-version"), timeout_seconds=30)
    if result.returncode != 0:
        raise RadioAudioAnalysisError(f"tool version check failed: {command}")
    lines = (result.stdout or result.stderr).splitlines()
    if not lines:
        raise RadioAudioAnalysisError(f"tool version check returned no output: {command}")
    return lines[0].strip()


def _station_assignments(curation: Mapping[str, Any]) -> dict[str, str]:
    stations = curation.get("stations")
    if not isinstance(stations, list) or not stations:
        raise RadioAudioAnalysisError("curation must contain a nonempty stations array")
    assignments: dict[str, str] = {}
    for raw_station in stations:
        if not isinstance(raw_station, Mapping) or not isinstance(raw_station.get("id"), str):
            raise RadioAudioAnalysisError("curation contains an invalid station")
        station_id = str(raw_station["id"])
        for field in ("pendingAssetIds", "approvedAssetIds", "rejectedAssetIds"):
            asset_ids = raw_station.get(field)
            if not isinstance(asset_ids, list) or any(not isinstance(item, str) for item in asset_ids):
                raise RadioAudioAnalysisError(f"curation station {station_id} has invalid {field}")
            for asset_id in asset_ids:
                if asset_id in assignments:
                    raise RadioAudioAnalysisError(f"curation assigns an asset more than once: {asset_id}")
                assignments[asset_id] = station_id
    return assignments


def load_radio_assets(
    repository_root: Path, inventory_path: Path, curation_path: Path
) -> tuple[list[RadioAsset], dict[str, str]]:
    """Bind every inventoried radio byte to exactly one curation station."""
    repository_root = repository_root.resolve()
    inventory = _load_json(inventory_path)
    curation = _load_json(curation_path)
    if inventory.get("schemaVersion") != 1 or inventory.get("assetRoot") != "assets":
        raise RadioAudioAnalysisError("content inventory identity is unsupported")
    if curation.get("schemaVersion") != 1 or curation.get("planId") != "vibesnake-content-curation-v1":
        raise RadioAudioAnalysisError("content curation identity is unsupported")
    if curation.get("inventoryPolicySha256") != inventory.get("policySha256"):
        raise RadioAudioAnalysisError("content curation does not match the inventory policy")
    assignments = _station_assignments(curation)
    raw_assets = inventory.get("assets")
    if not isinstance(raw_assets, list):
        raise RadioAudioAnalysisError("content inventory assets must be an array")
    asset_root = (repository_root / "assets").resolve(strict=True)
    assets: list[RadioAsset] = []
    for entry in raw_assets:
        if not isinstance(entry, Mapping):
            raise RadioAudioAnalysisError("content inventory contains a non-object asset")
        if entry.get("role") != "runtime-radio-track":
            continue
        asset_id = entry.get("id")
        relative_path = entry.get("path")
        expected_bytes = entry.get("bytes")
        expected_sha256 = entry.get("sha256")
        if (
            not isinstance(asset_id, str)
            or not isinstance(relative_path, str)
            or not isinstance(expected_bytes, int)
            or not isinstance(expected_sha256, str)
            or not re.fullmatch(r"[0-9a-f]{64}", expected_sha256)
        ):
            raise RadioAudioAnalysisError("content inventory contains an invalid radio asset")
        station_id = assignments.get(asset_id)
        if station_id is None:
            raise RadioAudioAnalysisError(f"curation does not assign radio asset: {asset_id}")
        source_path = asset_root.joinpath(*relative_path.split("/")).resolve(strict=True)
        try:
            source_path.relative_to(asset_root)
        except ValueError as error:
            raise RadioAudioAnalysisError(f"radio asset escapes the asset root: {relative_path}") from error
        if not source_path.is_file() or source_path.is_symlink():
            raise RadioAudioAnalysisError(f"radio asset must be a regular file: {relative_path}")
        assets.append(RadioAsset(asset_id, station_id, relative_path, expected_bytes, expected_sha256, source_path))
    if set(assignments) != {asset.asset_id for asset in assets}:
        raise RadioAudioAnalysisError("curation and inventory radio sets differ")
    if len(assets) != 95:
        raise RadioAudioAnalysisError(f"expected 95 radio assets, found {len(assets)}")
    return sorted(assets, key=lambda item: item.relative_path), {
        "inventorySha256": _sha256(inventory_path),
        "curationSha256": _sha256(curation_path),
        "inventoryPolicySha256": str(inventory["policySha256"]),
        "curationDecisionStatus": str(curation.get("decisionStatus")),
    }


def _analyze_asset(asset: RadioAsset, ffmpeg: str, ffprobe: str, timeout_seconds: int) -> dict[str, Any]:
    failures: list[str] = []
    actual_bytes = asset.source_path.stat().st_size
    source_sha256 = _sha256(asset.source_path)
    if actual_bytes != asset.expected_bytes:
        failures.append("source byte count differs from inventory")
    if source_sha256 != asset.expected_sha256:
        failures.append("source SHA-256 differs from inventory")
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
            os.fspath(asset.source_path),
        ),
        timeout_seconds,
    )
    if probe.returncode != 0:
        raise RadioAudioAnalysisError(f"ffprobe failed for {asset.relative_path}: {probe.stderr.strip()[-500:]}")
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
            os.fspath(asset.source_path),
            "-map",
            "0:a:0",
            "-af",
            (
                "ebur128=peak=true:framelog=verbose,volumedetect,"
                f"silencedetect=noise={SILENCE_NOISE_DBFS}dB:d={MINIMUM_REPORTED_SILENCE_SECONDS}"
            ),
            "-f",
            "null",
            "-",
        ),
        timeout_seconds,
    )
    if decode.returncode != 0:
        raise RadioAudioAnalysisError(f"full decode failed for {asset.relative_path}: {decode.stderr.strip()[-500:]}")
    measurement = parse_ffmpeg_output(decode.stderr)
    silence = parse_silence_output(decode.stderr, media["durationSeconds"])
    if _sha256(asset.source_path) != source_sha256:
        failures.append("source changed during analysis")
    if media["codec"] != "mp3":
        failures.append("codec is not MP3")
    if media["channels"] not in {1, 2}:
        failures.append("channel count is not mono or stereo")
    if not MINIMUM_SAMPLE_RATE_HZ <= media["sampleRateHz"] <= MAXIMUM_SAMPLE_RATE_HZ:
        failures.append("sample rate is outside the admitted range")
    if not MINIMUM_DURATION_SECONDS <= media["durationSeconds"] <= MAXIMUM_DURATION_SECONDS:
        failures.append("duration is outside the admitted range")
    minimum_loudness = TARGET_INTEGRATED_LUFS - LOUDNESS_TOLERANCE_LU
    maximum_loudness = TARGET_INTEGRATED_LUFS + LOUDNESS_TOLERANCE_LU
    if not minimum_loudness <= measurement["integratedLufs"] <= maximum_loudness:
        failures.append("integrated loudness is outside the admission band")
    if measurement["truePeakDbtp"] > MAXIMUM_TRUE_PEAK_DBTP:
        failures.append("true peak exceeds the admission ceiling")
    if silence["leadingSilenceSeconds"] > MAXIMUM_LEADING_SILENCE_SECONDS:
        failures.append("leading silence exceeds the admission ceiling")
    if silence["trailingSilenceSeconds"] > MAXIMUM_TRAILING_SILENCE_SECONDS:
        failures.append("trailing silence exceeds the admission ceiling")
    if silence["maximumInternalSilenceSeconds"] > MAXIMUM_INTERNAL_SILENCE_SECONDS:
        failures.append("internal silence exceeds the admission ceiling")
    gain = round(TARGET_INTEGRATED_LUFS - measurement["integratedLufs"], 1)
    predicted_peak = round(measurement["truePeakDbtp"] + gain, 1)
    return {
        "assetId": asset.asset_id,
        "stationId": asset.station_id,
        "path": asset.relative_path,
        "sourceBytes": actual_bytes,
        "sourceSha256": source_sha256,
        **media,
        **measurement,
        **silence,
        "recommendedLoudnessGainDb": gain,
        "predictedTruePeakAfterGainDbtp": predicted_peak,
        "limiterRequiredAtTarget": predicted_peak > MAXIMUM_TRUE_PEAK_DBTP,
        "passed": not failures,
        "failures": failures,
    }


def summarize_station(
    station_id: str,
    rows: Sequence[Mapping[str, Any]],
    decoder_error_count: int,
) -> dict[str, Any]:
    """Summarize one station without weakening any per-track failure."""
    measured_count = len(rows)

    def count_failure(reason: str) -> int:
        return sum(reason in row["failures"] for row in rows)

    def values(field: str) -> list[float]:
        return [float(row[field]) for row in rows]

    loudness = values("integratedLufs")
    true_peaks = values("truePeakDbtp")
    return {
        "stationId": station_id,
        "trackCount": measured_count + decoder_error_count,
        "measuredTrackCount": measured_count,
        "passedTrackCount": sum(bool(row["passed"]) for row in rows),
        "failedTrackCount": sum(not bool(row["passed"]) for row in rows) + decoder_error_count,
        "decoderErrorCount": decoder_error_count,
        "averageIntegratedLufs": round(sum(loudness) / measured_count, 2) if measured_count else None,
        "minimumIntegratedLufs": min(loudness, default=None),
        "maximumIntegratedLufs": max(loudness, default=None),
        "maximumTruePeakDbtp": max(true_peaks, default=None),
        "loudnessFailureCount": count_failure("integrated loudness is outside the admission band"),
        "truePeakFailureCount": count_failure("true peak exceeds the admission ceiling"),
        "leadingSilenceFailureCount": count_failure("leading silence exceeds the admission ceiling"),
        "trailingSilenceFailureCount": count_failure("trailing silence exceeds the admission ceiling"),
        "internalSilenceFailureCount": count_failure("internal silence exceeds the admission ceiling"),
    }


def apply_final_source_integrity_sweep(
    assets: Sequence[RadioAsset],
    source_hashes_before: Mapping[str, str],
    results: Sequence[dict[str, Any]],
    errors: Sequence[dict[str, Any]],
) -> list[str]:
    """Fail affected rows when any source changes during the whole campaign."""
    expected_paths = {asset.relative_path for asset in assets}
    if set(source_hashes_before) != expected_paths:
        raise RadioAudioAnalysisError("source-integrity baseline does not match the radio asset set")
    results_by_path = {str(row["path"]): row for row in results}
    errors_by_path = {str(error["path"]): error for error in errors}
    modified_paths: list[str] = []
    for asset in assets:
        if _sha256(asset.source_path) == source_hashes_before[asset.relative_path]:
            continue
        modified_paths.append(asset.relative_path)
        row = results_by_path.get(asset.relative_path)
        if row is not None:
            if "source changed during analysis" not in row["failures"]:
                row["failures"].append("source changed during analysis")
            row["passed"] = False
        error = errors_by_path.get(asset.relative_path)
        if error is not None:
            error["sourceChangedDuringAnalysis"] = True
    return sorted(modified_paths)


def _require_output_path(output_path: Path) -> Path:
    resolved = output_path.expanduser().resolve()
    if resolved.is_relative_to(PROJECT_ROOT) and not any(
        resolved.is_relative_to(root.resolve()) for root in ALLOWED_LOCAL_OUTPUT_ROOTS
    ):
        raise RadioAudioAnalysisError("output inside the repository must stay under TestResults or archive")
    return resolved


def _atomic_write_json(path: Path, value: Mapping[str, Any], replace: bool) -> None:
    if path.exists() and not replace:
        raise RadioAudioAnalysisError(f"output already exists; pass --replace to overwrite it: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(value, indent=2, sort_keys=True) + "\n"
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n", dir=path.parent, delete=False) as handle:
            temporary_name = handle.name
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_name, path)
    except OSError as error:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)
        raise RadioAudioAnalysisError(f"could not write qualification output: {error}") from error


def analyze_library(
    repository_root: Path,
    inventory_path: Path,
    curation_path: Path,
    ffmpeg: str,
    ffprobe: str,
    workers: int,
    timeout_seconds: int,
) -> dict[str, Any]:
    """Measure every curated radio asset and return review-ready evidence."""
    assets, input_identity = load_radio_assets(repository_root, inventory_path, curation_path)
    source_hashes_before = {asset.relative_path: _sha256(asset.source_path) for asset in assets}
    results: list[dict[str, Any]] = []
    errors: list[dict[str, Any]] = []

    def analyze(asset: RadioAsset) -> tuple[str, dict[str, Any] | None, str | None]:
        try:
            return asset.relative_path, _analyze_asset(asset, ffmpeg, ffprobe, timeout_seconds), None
        except RadioAudioAnalysisError as error:
            return asset.relative_path, None, str(error)

    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {executor.submit(analyze, asset): asset for asset in assets}
        for completed, future in enumerate(concurrent.futures.as_completed(futures), start=1):
            relative_path, result, error = future.result()
            print(f"Measured {completed}/{len(assets)}: {relative_path}", file=sys.stderr)
            if result is not None:
                results.append(result)
            else:
                errors.append({"path": relative_path, "error": error or "unknown analysis error"})
    results.sort(key=lambda item: str(item["path"]))
    errors.sort(key=lambda item: item["path"])
    modified_source_paths = apply_final_source_integrity_sweep(
        assets,
        source_hashes_before,
        results,
        errors,
    )
    station_by_path = {asset.relative_path: asset.station_id for asset in assets}
    station_summaries = []
    for station_id in sorted({asset.station_id for asset in assets}):
        rows = [row for row in results if row["stationId"] == station_id]
        station_errors = [error for error in errors if station_by_path[error["path"]] == station_id]
        station_summaries.append(summarize_station(station_id, rows, len(station_errors)))
    passed_count = sum(bool(row["passed"]) for row in results)
    source_bytes_modified = bool(modified_source_paths)

    def measured_values(field: str) -> list[float]:
        return [float(row[field]) for row in results]

    return {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-audio-qualification-v1",
        "measuredUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "passed": len(results) == len(assets) and passed_count == len(assets),
        "releaseApproved": False,
        "humanListeningRequired": True,
        "sourceBytesModified": source_bytes_modified,
        "measurementBasis": "Full decode with FFmpeg EBU R 128 and true-peak measurement",
        "admissionPolicy": {
            "targetIntegratedLufs": TARGET_INTEGRATED_LUFS,
            "loudnessToleranceLu": LOUDNESS_TOLERANCE_LU,
            "maximumTruePeakDbtp": MAXIMUM_TRUE_PEAK_DBTP,
            "silenceNoiseDbfs": SILENCE_NOISE_DBFS,
            "minimumReportedSilenceSeconds": MINIMUM_REPORTED_SILENCE_SECONDS,
            "maximumLeadingSilenceSeconds": MAXIMUM_LEADING_SILENCE_SECONDS,
            "maximumTrailingSilenceSeconds": MAXIMUM_TRAILING_SILENCE_SECONDS,
            "maximumInternalSilenceSeconds": MAXIMUM_INTERNAL_SILENCE_SECONDS,
            "minimumDurationSeconds": MINIMUM_DURATION_SECONDS,
            "maximumDurationSeconds": MAXIMUM_DURATION_SECONDS,
            "minimumSampleRateHz": MINIMUM_SAMPLE_RATE_HZ,
            "maximumSampleRateHz": MAXIMUM_SAMPLE_RATE_HZ,
            "allowedChannelCounts": [1, 2],
        },
        "toolchain": {
            "ffmpeg": _tool_version(ffmpeg),
            "ffprobe": _tool_version(ffprobe),
            "operatingSystem": platform.system(),
            "operatingSystemRelease": platform.release(),
            "architecture": platform.machine(),
            "workers": workers,
            "perTrackTimeoutSeconds": timeout_seconds,
        },
        "inputs": input_identity,
        "summary": {
            "expectedTrackCount": len(assets),
            "measuredTrackCount": len(results),
            "passedTrackCount": passed_count,
            "failedTrackCount": len(assets) - passed_count,
            "decoderErrorCount": len(errors),
            "modifiedSourceCount": len(modified_source_paths),
            "modifiedSourcePaths": modified_source_paths,
            "sourceBytes": sum(asset.expected_bytes for asset in assets),
            "totalDurationSeconds": round(sum(float(row["durationSeconds"]) for row in results), 3),
            "minimumIntegratedLufs": min(measured_values("integratedLufs"), default=None),
            "maximumIntegratedLufs": max(measured_values("integratedLufs"), default=None),
            "minimumTruePeakDbtp": min(measured_values("truePeakDbtp"), default=None),
            "maximumTruePeakDbtp": max(measured_values("truePeakDbtp"), default=None),
            "loudnessFailureCount": sum(
                "integrated loudness is outside the admission band" in row["failures"] for row in results
            ),
            "truePeakFailureCount": sum(
                "true peak exceeds the admission ceiling" in row["failures"] for row in results
            ),
            "leadingSilenceFailureCount": sum(
                "leading silence exceeds the admission ceiling" in row["failures"] for row in results
            ),
            "trailingSilenceFailureCount": sum(
                "trailing silence exceeds the admission ceiling" in row["failures"] for row in results
            ),
            "internalSilenceFailureCount": sum(
                "internal silence exceeds the admission ceiling" in row["failures"] for row in results
            ),
        },
        "stations": station_summaries,
        "tracks": results,
        "decoderErrors": errors,
    }


def main(argv: list[str] | None = None) -> int:
    """Analyze the exact public radio inventory and retain bounded evidence."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=PROJECT_ROOT)
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY)
    parser.add_argument("--curation", type=Path, default=DEFAULT_CURATION)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    parser.add_argument("--workers", type=int, default=min(4, os.cpu_count() or 1))
    parser.add_argument("--timeout-seconds", type=int, default=120)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    try:
        if not 1 <= args.workers <= MAXIMUM_WORKERS:
            raise RadioAudioAnalysisError(f"workers must be between 1 and {MAXIMUM_WORKERS}")
        if not 10 <= args.timeout_seconds <= 600:
            raise RadioAudioAnalysisError("timeout-seconds must be between 10 and 600")
        ffmpeg = shutil.which(args.ffmpeg)
        ffprobe = shutil.which(args.ffprobe)
        if ffmpeg is None or ffprobe is None:
            raise RadioAudioAnalysisError("ffmpeg and ffprobe must both be available")
        output = _require_output_path(args.output)
        evidence = analyze_library(
            args.repository_root,
            args.inventory,
            args.curation,
            ffmpeg,
            ffprobe,
            args.workers,
            args.timeout_seconds,
        )
        _atomic_write_json(output, evidence, args.replace)
    except RadioAudioAnalysisError as error:
        print(f"Radio audio analysis failed: {error}", file=sys.stderr)
        return 2
    summary = evidence["summary"]
    print(
        f"Radio audio evidence: tracks={summary['measuredTrackCount']}/{summary['expectedTrackCount']} "
        f"passed={summary['passedTrackCount']} failed={summary['failedTrackCount']} output={output}"
    )
    return 0 if evidence["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
