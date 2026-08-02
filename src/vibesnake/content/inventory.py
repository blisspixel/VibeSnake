"""Build and validate the deterministic source-asset inventory."""

from __future__ import annotations

from collections import Counter, defaultdict
from copy import deepcopy
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct
from typing import Any
import zlib


CONTENT_INVENTORY_SCHEMA_VERSION = 1
CONTENT_POLICY_SCHEMA_VERSION = 1

_MEDIA_TYPES = {
    ".csv": "text/csv",
    ".json": "application/json",
    ".md": "text/markdown",
    ".mp3": "audio/mpeg",
    ".png": "image/png",
    ".txt": "text/plain",
    ".wav": "audio/wav",
}
_RUNTIME_USES = {"none", "optional", "required"}
_SHIP_STATUSES = {"approved", "blocked", "excluded"}
_RIGHTS_STATUSES = {"cleared", "not-applicable", "unverified"}
_POLICY_FIELDS = {"schemaVersion", "assetRoot", "rules"}
_RULE_FIELDS = {
    "id",
    "patterns",
    "role",
    "packId",
    "runtimeUse",
    "shipStatus",
    "rights",
}
_RIGHTS_FIELDS = {"status", "source", "license", "attribution", "reviewNote"}
_MAX_INSPECTED_FILE_BYTES = 256 * 1024 * 1024
_MAX_PNG_DIMENSION = 16_384
_MAX_PNG_PIXELS = 67_108_864
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
_PNG_CRITICAL_CHUNKS = {b"IHDR", b"PLTE", b"IDAT", b"IEND"}


class ContentInventoryError(ValueError):
    """Raised when content policy or inventory state is unsafe or ambiguous."""


@dataclass(frozen=True)
class _PngHeader:
    """Decoded PNG header fields required for bounded image-data validation."""

    width: int
    height: int
    bit_depth: int
    color_type: int
    interlace: int


class _PngImageDataValidator:
    """Validate a PNG deflate stream without materializing the decoded image."""

    _OUTPUT_CHUNK_BYTES = 64 * 1024

    def __init__(self, header: _PngHeader) -> None:
        self._row_payload_bytes = _png_scanline_lengths(header)
        self._row_index = 0
        self._row_offset = 0
        self._decompressor = zlib.decompressobj()

    def feed(self, compressed: bytes) -> None:
        """Consume one bounded piece of consecutive IDAT data."""
        pending = compressed
        while pending:
            try:
                decoded = self._decompressor.decompress(
                    pending,
                    self._OUTPUT_CHUNK_BYTES,
                )
            except zlib.error as error:
                raise ValueError(f"PNG image data is not valid zlib data: {error}") from error
            pending = self._decompressor.unconsumed_tail
            self._consume_scanlines(decoded)
            if self._decompressor.unused_data:
                raise ValueError("PNG image data contains bytes after the zlib stream")

    def finish(self) -> None:
        """Require a complete zlib stream with exactly the expected scanlines."""
        while True:
            try:
                decoded = self._decompressor.decompress(
                    b"",
                    self._OUTPUT_CHUNK_BYTES,
                )
            except zlib.error as error:
                raise ValueError(f"PNG image data is not valid zlib data: {error}") from error
            if not decoded:
                break
            self._consume_scanlines(decoded)

        if not self._decompressor.eof:
            raise ValueError("PNG image data has an incomplete zlib stream")
        if self._decompressor.unused_data:
            raise ValueError("PNG image data contains bytes after the zlib stream")
        if self._row_index != len(self._row_payload_bytes) or self._row_offset:
            raise ValueError("PNG image data does not contain every expected scanline")

    def _consume_scanlines(self, decoded: bytes) -> None:
        cursor = 0
        while cursor < len(decoded):
            if self._row_index >= len(self._row_payload_bytes):
                raise ValueError("PNG image data exceeds the expected decoded size")

            row_bytes = self._row_payload_bytes[self._row_index]
            row_total = row_bytes + 1
            if self._row_offset == 0:
                filter_method = decoded[cursor]
                if filter_method > 4:
                    raise ValueError(f"PNG scanline uses invalid filter method {filter_method}")
                cursor += 1
                self._row_offset = 1

            consumed = min(len(decoded) - cursor, row_total - self._row_offset)
            cursor += consumed
            self._row_offset += consumed
            if self._row_offset == row_total:
                self._row_index += 1
                self._row_offset = 0


def _png_scanline_lengths(header: _PngHeader) -> tuple[int, ...]:
    """Return decoded payload bytes for each regular or Adam7 scanline."""
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[header.color_type]
    bits_per_pixel = channels * header.bit_depth
    if header.interlace == 0:
        row_bytes = (header.width * bits_per_pixel + 7) // 8
        return (row_bytes,) * header.height

    rows: list[int] = []
    for start_x, start_y, step_x, step_y in (
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2),
    ):
        if header.width <= start_x or header.height <= start_y:
            continue
        pass_width = (header.width - start_x + step_x - 1) // step_x
        pass_height = (header.height - start_y + step_y - 1) // step_y
        row_bytes = (pass_width * bits_per_pixel + 7) // 8
        rows.extend([row_bytes] * pass_height)
    return tuple(rows)


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _inspect_integrity(path: Path, media_type: str, size: int) -> tuple[str, str]:
    if size == 0:
        return "empty", "file contains zero bytes"
    if size > _MAX_INSPECTED_FILE_BYTES:
        return "invalid", f"file exceeds the {_MAX_INSPECTED_FILE_BYTES}-byte inspection limit"

    detail = "basic structure check passed"
    try:
        if media_type == "application/json":
            json.loads(path.read_text(encoding="utf-8"))
        elif media_type in {"text/csv", "text/markdown", "text/plain"}:
            path.read_text(encoding="utf-8")
        elif media_type == "image/png":
            valid, detail = _inspect_png(path, size)
            if not valid:
                return "invalid", detail
        elif media_type == "audio/wav":
            valid, detail = _inspect_wav(path, size)
            if not valid:
                return "invalid", detail
        elif media_type == "audio/mpeg":
            valid, detail = _inspect_mp3(path, size)
            if not valid:
                return "invalid", detail
    except (OSError, UnicodeError, ValueError, json.JSONDecodeError) as error:
        return "invalid", f"{type(error).__name__}: {error}"
    return "valid", detail


def _inspect_png(path: Path, size: int) -> tuple[bool, str]:
    with path.open("rb") as source:
        if source.read(len(_PNG_SIGNATURE)) != _PNG_SIGNATURE:
            return False, "PNG signature is invalid"

        cursor = len(_PNG_SIGNATURE)
        chunk_index = 0
        saw_header = False
        saw_palette = False
        saw_image_data = False
        image_data_closed = False
        saw_end = False
        saw_c2pa_container = False
        header: _PngHeader | None = None
        image_data_validator: _PngImageDataValidator | None = None
        while cursor < size:
            chunk_header = source.read(8)
            if len(chunk_header) != 8:
                return False, "PNG chunk header is truncated"
            chunk_size = struct.unpack(">I", chunk_header[:4])[0]
            chunk_type = chunk_header[4:]
            cursor += 8
            if not all(ord("A") <= byte <= ord("Z") or ord("a") <= byte <= ord("z") for byte in chunk_type):
                return False, "PNG chunk type is invalid"
            if chunk_size > size - cursor - 4:
                return False, "PNG chunk data is truncated"
            if chunk_index == 0 and chunk_type != b"IHDR":
                return False, "PNG IHDR must be the first chunk"
            if chunk_type[0] & 0x20 == 0 and chunk_type not in _PNG_CRITICAL_CHUNKS:
                return False, "PNG contains an unsupported critical chunk"
            if chunk_type == b"PLTE" and (saw_palette or saw_image_data):
                return False, "PNG PLTE must occur at most once and before IDAT"
            if chunk_type == b"IDAT" and header is not None:
                if header.color_type == 3 and not saw_palette:
                    return False, "PNG indexed-color image requires PLTE before IDAT"

            checksum = zlib.crc32(chunk_type)
            captured_data = bytearray() if chunk_type in {b"IHDR", b"PLTE"} else None
            remaining = chunk_size
            while remaining:
                data = source.read(min(remaining, 64 * 1024))
                if not data:
                    return False, "PNG chunk data is truncated"
                checksum = zlib.crc32(data, checksum)
                if captured_data is not None:
                    captured_data.extend(data)
                if chunk_type == b"IDAT":
                    if image_data_validator is None:
                        return False, "PNG IDAT occurs before a valid IHDR"
                    try:
                        image_data_validator.feed(data)
                    except ValueError as error:
                        return False, str(error)
                remaining -= len(data)
            cursor += chunk_size

            raw_checksum = source.read(4)
            if len(raw_checksum) != 4:
                return False, "PNG chunk CRC is truncated"
            cursor += 4
            if struct.unpack(">I", raw_checksum)[0] != checksum:
                return False, f"PNG {chunk_type.decode('ascii')} chunk CRC is invalid"

            if chunk_type == b"IHDR":
                if saw_header or captured_data is None or len(captured_data) != 13:
                    return False, "PNG IHDR chunk is invalid"
                header, detail = _validate_png_header(bytes(captured_data))
                if header is None:
                    return False, detail
                saw_header = True
                image_data_validator = _PngImageDataValidator(header)
            elif chunk_type == b"PLTE":
                if header is None or captured_data is None:
                    return False, "PNG PLTE occurs before a valid IHDR"
                valid, detail = _validate_png_palette(header, bytes(captured_data))
                if not valid:
                    return False, detail
                saw_palette = True
            elif chunk_type == b"IDAT":
                if image_data_closed:
                    return False, "PNG IDAT chunks must be consecutive"
                saw_image_data = saw_image_data or chunk_size > 0
            elif chunk_type == b"caBX":
                saw_c2pa_container = True
                if saw_image_data:
                    image_data_closed = True
            elif saw_image_data:
                image_data_closed = True

            if chunk_type == b"IEND":
                if chunk_size != 0:
                    return False, "PNG IEND chunk must be empty"
                if cursor != size:
                    return False, "PNG contains trailing bytes after IEND"
                saw_end = True
                break
            chunk_index += 1

    if not saw_header:
        return False, "PNG stream has no IHDR chunk"
    if not saw_image_data:
        return False, "PNG stream has no image data"
    if not saw_end:
        return False, "PNG stream has no IEND chunk"
    if image_data_validator is None:
        return False, "PNG stream has no image-data validator"
    try:
        image_data_validator.finish()
    except ValueError as error:
        return False, str(error)
    detail = "PNG container, palette, compressed scanlines, and chunk CRCs are valid"
    if saw_c2pa_container:
        detail += "; caBX C2PA/JUMBF provenance container is present"
    return True, detail


def _validate_png_header(header: bytes) -> tuple[_PngHeader | None, str]:
    width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(">IIBBBBB", header)
    if width == 0 or height == 0:
        return None, "PNG dimensions must be positive"
    if width > _MAX_PNG_DIMENSION or height > _MAX_PNG_DIMENSION:
        return None, f"PNG dimension exceeds {_MAX_PNG_DIMENSION} pixels"
    if width * height > _MAX_PNG_PIXELS:
        return None, f"PNG pixel count exceeds {_MAX_PNG_PIXELS}"

    allowed_depths = {
        0: {1, 2, 4, 8, 16},
        2: {8, 16},
        3: {1, 2, 4, 8},
        4: {8, 16},
        6: {8, 16},
    }
    if bit_depth not in allowed_depths.get(color_type, set()):
        return None, "PNG color type and bit depth are incompatible"
    if compression != 0 or filtering != 0 or interlace not in {0, 1}:
        return None, "PNG compression, filter, or interlace method is unsupported"
    return _PngHeader(width, height, bit_depth, color_type, interlace), "PNG IHDR is valid"


def _validate_png_palette(header: _PngHeader, palette: bytes) -> tuple[bool, str]:
    """Validate PNG palette presence, placement, and entry count."""
    if header.color_type in {0, 4}:
        return False, "PNG grayscale images cannot contain PLTE"
    if not palette or len(palette) % 3 or len(palette) > 768:
        return False, "PNG PLTE must contain between 1 and 256 RGB entries"
    if header.color_type == 3 and len(palette) // 3 > 1 << header.bit_depth:
        return False, "PNG PLTE has more entries than its indexed bit depth allows"
    return True, "PNG PLTE is valid"


def _inspect_wav(path: Path, size: int) -> tuple[bool, str]:
    with path.open("rb") as source:
        header = source.read(12)
        if len(header) != 12 or header[:4] != b"RIFF" or header[8:] != b"WAVE":
            return False, "WAV RIFF header is invalid"

        format_valid = False
        data_bytes = 0
        cursor = 12
        while cursor + 8 <= size:
            source.seek(cursor)
            chunk_header = source.read(8)
            if len(chunk_header) != 8:
                return False, "WAV chunk header is truncated"
            chunk_id = chunk_header[:4]
            chunk_size = struct.unpack("<I", chunk_header[4:])[0]
            chunk_start = cursor + 8
            chunk_end = chunk_start + chunk_size
            if chunk_end > size:
                return False, "WAV chunk extends beyond the file"
            if chunk_id == b"fmt ":
                if chunk_size < 16:
                    return False, "WAV format chunk is too short"
                source.seek(chunk_start)
                audio_format, channels, sample_rate, _, block_align, bits = struct.unpack("<HHIIHH", source.read(16))
                format_valid = (
                    audio_format in {1, 3, 0xFFFE} and channels > 0 and sample_rate > 0 and block_align > 0 and bits > 0
                )
            elif chunk_id == b"data":
                data_bytes += chunk_size
            cursor = chunk_end + (chunk_size % 2)

    if not format_valid:
        return False, "WAV stream has no supported format chunk"
    if data_bytes == 0:
        return False, "WAV stream has no audio data"
    return True, "basic structure check passed"


def _inspect_mp3(path: Path, size: int) -> tuple[bool, str]:
    with path.open("rb") as source:
        header = source.read(10)
        frame_offset = 0
        if header[:3] == b"ID3":
            if len(header) < 10 or any(byte & 0x80 for byte in header[6:10]):
                return False, "MP3 ID3 header is invalid"
            tag_size = sum(byte << shift for byte, shift in zip(header[6:10], (21, 14, 7, 0)))
            frame_offset = 10 + tag_size
            if header[5] & 0x10:
                frame_offset += 10
        if frame_offset + 4 > size:
            return False, "MP3 stream has no complete MPEG audio frame"

        expected_format: tuple[int, int, int] | None = None
        for _ in range(2):
            source.seek(frame_offset)
            frame_header = source.read(4)
            frame = _mp3_frame_contract(frame_header)
            if frame is None:
                return False, "MP3 stream lacks two consecutive MPEG audio frames"
            frame_length, version, layer, sample_rate = frame
            if frame_offset + frame_length > size:
                return False, "MP3 stream has an incomplete MPEG audio frame"
            current_format = (version, layer, sample_rate)
            if expected_format is not None and current_format != expected_format:
                return False, "MP3 consecutive frames use incompatible stream parameters"
            expected_format = current_format
            frame_offset += frame_length

    return True, "MP3 stream contains two consecutive complete MPEG audio frames"


def _mp3_frame_contract(header: bytes) -> tuple[int, int, int, int] | None:
    if len(header) != 4:
        return None
    value = int.from_bytes(header, "big")
    if value & 0xFFE00000 != 0xFFE00000:
        return None

    version = (value >> 19) & 0x03
    layer = (value >> 17) & 0x03
    bitrate_index = (value >> 12) & 0x0F
    sample_rate_index = (value >> 10) & 0x03
    padding = (value >> 9) & 0x01
    if version == 1 or layer == 0 or bitrate_index in {0, 15} or sample_rate_index == 3:
        return None

    mpeg1_bitrates = {
        3: (32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448),
        2: (32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384),
        1: (32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320),
    }
    mpeg2_bitrates = {
        3: (32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256),
        2: (8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160),
        1: (8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160),
    }
    bitrate_table = mpeg1_bitrates if version == 3 else mpeg2_bitrates
    bitrate = bitrate_table[layer][bitrate_index - 1] * 1_000
    base_sample_rate = (44_100, 48_000, 32_000)[sample_rate_index]
    sample_rate = base_sample_rate // (1 if version == 3 else 2 if version == 2 else 4)

    if layer == 3:
        frame_length = ((12 * bitrate) // sample_rate + padding) * 4
    elif layer == 1 and version != 3:
        frame_length = (72 * bitrate) // sample_rate + padding
    else:
        frame_length = (144 * bitrate) // sample_rate + padding
    if frame_length < 4:
        return None
    return frame_length, version, layer, sample_rate


def _require_object(value: Any, location: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ContentInventoryError(f"{location} must be a JSON object")
    return value


def _require_exact_fields(value: dict[str, Any], expected: set[str], location: str) -> None:
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
    raise ContentInventoryError(f"{location} has invalid fields: {'; '.join(details)}")


def _require_text(value: Any, location: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ContentInventoryError(f"{location} must be a non-empty string")
    return value


def _validate_relative_path(value: Any, location: str, *, allow_glob: bool) -> str:
    text = _require_text(value, location)
    if "\\" in text or text.startswith("/"):
        raise ContentInventoryError(f"{location} must use a relative POSIX path")
    parts = PurePosixPath(text).parts
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ContentInventoryError(f"{location} contains an unsafe path segment")
    if not allow_glob and any(character in text for character in "*?["):
        raise ContentInventoryError(f"{location} cannot contain glob characters")
    return text


def _glob_regex(pattern: str) -> re.Pattern[str]:
    pieces = ["^"]
    index = 0
    while index < len(pattern):
        character = pattern[index]
        if character == "*":
            if index + 1 < len(pattern) and pattern[index + 1] == "*":
                pieces.append(".*")
                index += 2
            else:
                pieces.append("[^/]*")
                index += 1
        elif character == "?":
            pieces.append("[^/]")
            index += 1
        else:
            pieces.append(re.escape(character))
            index += 1
    pieces.append("$")
    return re.compile("".join(pieces))


def load_policy(policy_path: Path) -> dict[str, Any]:
    """Load and strictly validate a content policy document."""
    try:
        policy = json.loads(policy_path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise ContentInventoryError(f"content policy does not exist: {policy_path}") from error
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ContentInventoryError(f"content policy is unreadable: {policy_path}: {error}") from error

    policy = _require_object(policy, "content policy")
    _require_exact_fields(policy, _POLICY_FIELDS, "content policy")
    if policy["schemaVersion"] != CONTENT_POLICY_SCHEMA_VERSION:
        raise ContentInventoryError(f"unsupported content policy schema: {policy['schemaVersion']}")
    policy["assetRoot"] = _validate_relative_path(policy["assetRoot"], "content policy assetRoot", allow_glob=False)

    rules = policy["rules"]
    if not isinstance(rules, list) or not rules:
        raise ContentInventoryError("content policy rules must be a non-empty array")

    seen_rule_ids: set[str] = set()
    validated_rules = []
    for index, raw_rule in enumerate(rules):
        location = f"content policy rule {index}"
        rule = _require_object(raw_rule, location)
        _require_exact_fields(rule, _RULE_FIELDS, location)
        rule_id = _require_text(rule["id"], f"{location} id")
        if rule_id in seen_rule_ids:
            raise ContentInventoryError(f"duplicate content policy rule id: {rule_id}")
        seen_rule_ids.add(rule_id)

        patterns = rule["patterns"]
        if not isinstance(patterns, list) or not patterns:
            raise ContentInventoryError(f"{location} patterns must be a non-empty array")
        validated_patterns = []
        for pattern_index, pattern in enumerate(patterns):
            validated_patterns.append(
                _validate_relative_path(
                    pattern,
                    f"{location} pattern {pattern_index}",
                    allow_glob=True,
                )
            )

        role = _require_text(rule["role"], f"{location} role")
        pack_id = _require_text(rule["packId"], f"{location} packId")
        runtime_use = _require_text(rule["runtimeUse"], f"{location} runtimeUse")
        if runtime_use not in _RUNTIME_USES:
            raise ContentInventoryError(f"{location} has invalid runtimeUse: {runtime_use}")
        ship_status = _require_text(rule["shipStatus"], f"{location} shipStatus")
        if ship_status not in _SHIP_STATUSES:
            raise ContentInventoryError(f"{location} has invalid shipStatus: {ship_status}")

        rights = _require_object(rule["rights"], f"{location} rights")
        _require_exact_fields(rights, _RIGHTS_FIELDS, f"{location} rights")
        rights_status = _require_text(rights["status"], f"{location} rights status")
        if rights_status not in _RIGHTS_STATUSES:
            raise ContentInventoryError(f"{location} has invalid rights status: {rights_status}")
        for field in sorted(_RIGHTS_FIELDS - {"status"}):
            _require_text(rights[field], f"{location} rights {field}")

        if ship_status == "approved" and rights_status != "cleared":
            raise ContentInventoryError(f"{location} cannot approve shipping without cleared rights")
        if ship_status == "excluded" and runtime_use != "none":
            raise ContentInventoryError(f"{location} cannot exclude an asset used by the runtime")

        validated_rules.append(
            {
                "id": rule_id,
                "patterns": validated_patterns,
                "role": role,
                "packId": pack_id,
                "runtimeUse": runtime_use,
                "shipStatus": ship_status,
                "rights": deepcopy(rights),
            }
        )

    return {
        "schemaVersion": CONTENT_POLICY_SCHEMA_VERSION,
        "assetRoot": policy["assetRoot"],
        "rules": validated_rules,
    }


def _repository_relative(repository_root: Path, path: Path, location: str) -> str:
    try:
        return path.resolve().relative_to(repository_root.resolve()).as_posix()
    except ValueError as error:
        raise ContentInventoryError(f"{location} must be inside the repository") from error


def _inventory_files(asset_root: Path) -> list[tuple[Path, str]]:
    if not asset_root.is_dir():
        raise ContentInventoryError(f"asset root does not exist: {asset_root}")

    paths = sorted(asset_root.rglob("*"), key=lambda path: path.as_posix().casefold())
    for path in paths:
        if path.is_symlink():
            raise ContentInventoryError(f"asset tree cannot contain a symbolic link: {path}")

    files = [(path, path.relative_to(asset_root).as_posix()) for path in paths if path.is_file()]
    if not files:
        raise ContentInventoryError(f"asset root contains no files: {asset_root}")

    seen_casefolded: dict[str, str] = {}
    for _, relative_path in files:
        folded = relative_path.casefold()
        previous = seen_casefolded.get(folded)
        if previous is not None:
            raise ContentInventoryError(
                f"asset paths collide on case-insensitive systems: {previous} and {relative_path}"
            )
        seen_casefolded[folded] = relative_path
    return files


def build_inventory(repository_root: Path, policy_path: Path | None = None) -> dict[str, Any]:
    """Build a deterministic inventory for every file under the policy asset root."""
    repository_root = repository_root.resolve()
    if policy_path is None:
        policy_path = repository_root / "config" / "content_policy.json"
    policy_path = policy_path.resolve()
    policy_relative = _repository_relative(repository_root, policy_path, "content policy")
    policy = load_policy(policy_path)
    asset_root = (repository_root / policy["assetRoot"]).resolve()
    _repository_relative(repository_root, asset_root, "asset root")

    compiled_rules = [(rule, [_glob_regex(pattern) for pattern in rule["patterns"]]) for rule in policy["rules"]]
    rule_hits: Counter[str] = Counter()
    entries = []
    for path, relative_path in _inventory_files(asset_root):
        matching_rules = [
            rule for rule, patterns in compiled_rules if any(pattern.fullmatch(relative_path) for pattern in patterns)
        ]
        if not matching_rules:
            raise ContentInventoryError(f"asset has no content policy rule: {relative_path}")
        if len(matching_rules) > 1:
            rule_ids = ", ".join(rule["id"] for rule in matching_rules)
            raise ContentInventoryError(f"asset matches multiple content policy rules: {relative_path}: {rule_ids}")

        rule = matching_rules[0]
        rule_hits[rule["id"]] += 1
        suffix = path.suffix.lower()
        media_type = _MEDIA_TYPES.get(suffix)
        if media_type is None:
            raise ContentInventoryError(f"asset has an unsupported media extension: {relative_path}")
        size = path.stat().st_size
        integrity_status, integrity_detail = _inspect_integrity(path, media_type, size)
        if rule["shipStatus"] == "approved" and integrity_status != "valid":
            raise ContentInventoryError(
                f"approved asset failed integrity validation: {relative_path}: {integrity_detail}"
            )
        entries.append(
            {
                "id": f"asset:{relative_path}",
                "path": relative_path,
                "mediaType": media_type,
                "bytes": size,
                "sha256": _sha256_file(path),
                "integrityStatus": integrity_status,
                "integrityDetail": integrity_detail,
                "role": rule["role"],
                "packId": rule["packId"],
                "runtimeUse": rule["runtimeUse"],
                "shipStatus": rule["shipStatus"],
                "exportEligible": rule["shipStatus"] == "approved",
                "rights": deepcopy(rule["rights"]),
                "policyRule": rule["id"],
                "duplicateOf": None,
            }
        )

    unused_rules = sorted(rule["id"] for rule in policy["rules"] if not rule_hits[rule["id"]])
    if unused_rules:
        raise ContentInventoryError(f"content policy rules match no assets: {', '.join(unused_rules)}")

    duplicate_groups: dict[tuple[int, str], list[dict[str, Any]]] = defaultdict(list)
    for entry in entries:
        duplicate_groups[(entry["bytes"], entry["sha256"])].append(entry)
    repeated_groups = [group for group in duplicate_groups.values() if len(group) > 1]
    for group in repeated_groups:
        canonical_id = group[0]["id"]
        for entry in group[1:]:
            entry["duplicateOf"] = canonical_id

    summary = {
        "byIntegrityStatus": _count_by(entries, "integrityStatus"),
        "byMediaType": _count_by(entries, "mediaType"),
        "byPackId": _count_by(entries, "packId"),
        "byRightsStatus": _count_by_nested(entries, "rights", "status"),
        "byRole": _count_by(entries, "role"),
        "byShipStatus": _count_by(entries, "shipStatus"),
        "duplicateFileCount": sum(len(group) - 1 for group in repeated_groups),
        "duplicateGroupCount": len(repeated_groups),
        "exportEligibleBytes": sum(entry["bytes"] for entry in entries if entry["exportEligible"]),
        "exportEligibleFileCount": sum(1 for entry in entries if entry["exportEligible"]),
    }
    return {
        "schemaVersion": CONTENT_INVENTORY_SCHEMA_VERSION,
        "assetRoot": policy["assetRoot"],
        "policyPath": policy_relative,
        "policySha256": _sha256_file(policy_path),
        "fileCount": len(entries),
        "totalBytes": sum(entry["bytes"] for entry in entries),
        "summary": summary,
        "assets": entries,
    }


def _count_by(entries: list[dict[str, Any]], field: str) -> dict[str, int]:
    counts = Counter(str(entry[field]) for entry in entries)
    return dict(sorted(counts.items()))


def _count_by_nested(entries: list[dict[str, Any]], outer_field: str, inner_field: str) -> dict[str, int]:
    counts = Counter(str(entry[outer_field][inner_field]) for entry in entries)
    return dict(sorted(counts.items()))


def render_inventory(inventory: dict[str, Any]) -> str:
    """Render the canonical checked-in representation."""
    return json.dumps(inventory, ensure_ascii=False, indent=2) + "\n"


def write_inventory(
    repository_root: Path,
    policy_path: Path | None = None,
    inventory_path: Path | None = None,
) -> dict[str, Any]:
    """Regenerate the checked-in inventory from source files and policy."""
    repository_root = repository_root.resolve()
    if inventory_path is None:
        inventory_path = repository_root / "config" / "content_inventory.json"
    inventory_path = inventory_path.resolve()
    _repository_relative(repository_root, inventory_path, "content inventory")
    inventory = build_inventory(repository_root, policy_path)
    inventory_path.parent.mkdir(parents=True, exist_ok=True)
    inventory_path.write_text(render_inventory(inventory), encoding="utf-8", newline="\n")
    return inventory


def check_inventory(
    repository_root: Path,
    policy_path: Path | None = None,
    inventory_path: Path | None = None,
) -> dict[str, Any]:
    """Require the checked-in inventory to exactly match current bytes and policy."""
    repository_root = repository_root.resolve()
    if inventory_path is None:
        inventory_path = repository_root / "config" / "content_inventory.json"
    inventory_path = inventory_path.resolve()
    _repository_relative(repository_root, inventory_path, "content inventory")
    expected = build_inventory(repository_root, policy_path)
    try:
        actual_text = inventory_path.read_text(encoding="utf-8")
    except FileNotFoundError as error:
        raise ContentInventoryError(f"content inventory does not exist: {inventory_path}; regenerate it") from error
    except (OSError, UnicodeError) as error:
        raise ContentInventoryError(f"content inventory is unreadable: {inventory_path}: {error}") from error
    if actual_text != render_inventory(expected):
        raise ContentInventoryError("content inventory is stale; run python scripts/content_inventory.py --write")
    return expected


def release_blockers(inventory: dict[str, Any]) -> list[str]:
    """Return deterministic reasons the inventoried source tree is not release-ready."""
    blockers = []
    for entry in inventory.get("assets", []):
        if entry["runtimeUse"] != "none" and entry["shipStatus"] != "approved":
            blockers.append(f"{entry['path']}: runtime asset is {entry['shipStatus']} for shipping")
        if entry["runtimeUse"] != "none" and entry["integrityStatus"] != "valid":
            blockers.append(f"{entry['path']}: runtime asset integrity is {entry['integrityStatus']}")
        if entry["exportEligible"] and entry["rights"]["status"] != "cleared":
            blockers.append(f"{entry['path']}: export-eligible asset lacks cleared rights")
        if entry["exportEligible"] and entry["duplicateOf"] is not None:
            blockers.append(f"{entry['path']}: export-eligible duplicate of {entry['duplicateOf']}")
    return blockers
