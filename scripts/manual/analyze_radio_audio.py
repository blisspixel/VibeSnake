"""Shared radio measurement library for review-copy preparation.

The full-library qualification campaign is the native `radio-audio` command.
It stays outside combined `all` and ordinary CI. This module parses probe,
loudness, and silence output and binds inventoried radio assets to curation
stations. It does not decode the library, write qualification evidence, or
approve a track.
"""

from __future__ import annotations

import hashlib
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping


MAXIMUM_JSON_BYTES = 8 * 1024 * 1024
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
