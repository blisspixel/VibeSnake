"""Contracts for deterministic asset inventory and release blocking."""

from __future__ import annotations

from copy import deepcopy
import json
from pathlib import Path
import struct
import zlib

import pytest

import vibesnake.content.inventory as inventory_module
from vibesnake.content.inventory import (
    CONTENT_INVENTORY_SCHEMA_VERSION,
    ContentInventoryError,
    build_inventory,
    check_inventory,
    load_policy,
    release_blockers,
    render_inventory,
    write_inventory,
)


CLEARED_RIGHTS = {
    "status": "cleared",
    "source": "test fixture",
    "license": "MIT",
    "attribution": "none",
    "reviewNote": "fixture rights are explicit",
}
UNVERIFIED_RIGHTS = {
    "status": "unverified",
    "source": "unknown fixture source",
    "license": "UNVERIFIED",
    "attribution": "REVIEW_REQUIRED",
    "reviewNote": "fixture is intentionally blocked",
}


def _png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    checksum = zlib.crc32(chunk_type)
    checksum = zlib.crc32(data, checksum)
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", checksum)


def _png(width: int = 2, height: int = 3, *, include_c2pa: bool = False) -> bytes:
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    scanlines = b"".join(b"\x00" + (b"\x00\x00\x00" * width) for _ in range(height))
    provenance = _png_chunk(b"caBX", b"test-jumbf") if include_c2pa else b""
    return (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", header)
        + provenance
        + _png_chunk(b"IDAT", zlib.compress(scanlines))
        + _png_chunk(b"IEND", b"")
    )


def _indexed_png(*, include_palette: bool = True, image_data: bytes | None = None) -> bytes:
    header = struct.pack(">IIBBBBB", 2, 1, 1, 3, 0, 0, 0)
    chunks = [_png_chunk(b"IHDR", header)]
    if include_palette:
        chunks.append(_png_chunk(b"PLTE", b"\x00\x00\x00\xff\xff\xff"))
    scanline = zlib.compress(b"\x00\x00") if image_data is None else image_data
    chunks.extend((_png_chunk(b"IDAT", scanline), _png_chunk(b"IEND", b"")))
    return b"\x89PNG\r\n\x1a\n" + b"".join(chunks)


def _mp3_frame() -> bytes:
    header = b"\xff\xfb\x90\x64"
    frame_length = (144 * 128_000) // 44_100
    return header + (b"\x00" * (frame_length - len(header)))


def _rule(
    rule_id: str,
    pattern: str,
    *,
    runtime_use: str = "required",
    ship_status: str = "approved",
    rights: dict[str, str] | None = None,
) -> dict[str, object]:
    return {
        "id": rule_id,
        "patterns": [pattern],
        "role": "test-role",
        "packId": "test-pack",
        "runtimeUse": runtime_use,
        "shipStatus": ship_status,
        "rights": deepcopy(rights if rights is not None else CLEARED_RIGHTS),
    }


def _write_policy(root: Path, rules: list[dict[str, object]]) -> Path:
    policy_path = root / "config" / "content_policy.json"
    policy_path.parent.mkdir(parents=True, exist_ok=True)
    policy_path.write_text(
        json.dumps({"schemaVersion": 1, "assetRoot": "assets", "rules": rules}),
        encoding="utf-8",
    )
    return policy_path


def test_build_inventory_is_sorted_hashed_and_reports_duplicates(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    (assets / "notes").mkdir(parents=True)
    (assets / "notes" / "b.txt").write_text("same", encoding="utf-8")
    (assets / "notes" / "a.txt").write_text("same", encoding="utf-8")
    policy_path = _write_policy(tmp_path, [_rule("notes", "notes/*.txt")])

    inventory = build_inventory(tmp_path, policy_path)

    assert inventory["schemaVersion"] == CONTENT_INVENTORY_SCHEMA_VERSION
    assert inventory["fileCount"] == 2
    assert inventory["totalBytes"] == 8
    assert [entry["path"] for entry in inventory["assets"]] == [
        "notes/a.txt",
        "notes/b.txt",
    ]
    assert inventory["assets"][0]["duplicateOf"] is None
    assert inventory["assets"][1]["duplicateOf"] == "asset:notes/a.txt"
    assert inventory["summary"]["duplicateGroupCount"] == 1
    assert inventory["summary"]["duplicateFileCount"] == 1
    assert len(inventory["assets"][0]["sha256"]) == 64
    assert release_blockers(inventory) == ["notes/b.txt: export-eligible duplicate of asset:notes/a.txt"]


def test_png_integrity_surfaces_embedded_c2pa_container(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "credentialed.png").write_bytes(_png(include_c2pa=True))
    policy_path = _write_policy(tmp_path, [_rule("image", "credentialed.png")])

    inventory = build_inventory(tmp_path, policy_path)

    assert "C2PA/JUMBF" in inventory["assets"][0]["integrityDetail"]


def test_write_and_check_require_exact_current_inventory(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    asset = assets / "config.json"
    asset.write_text("{}", encoding="utf-8")
    policy_path = _write_policy(tmp_path, [_rule("config", "config.json")])
    inventory_path = tmp_path / "config" / "content_inventory.json"

    written = write_inventory(tmp_path, policy_path, inventory_path)
    assert check_inventory(tmp_path, policy_path, inventory_path) == written
    assert inventory_path.read_text(encoding="utf-8") == render_inventory(written)

    asset.write_text('{"changed": true}', encoding="utf-8")
    with pytest.raises(ContentInventoryError, match="stale"):
        check_inventory(tmp_path, policy_path, inventory_path)


def test_write_failure_preserves_inventory_and_cleans_temporary_file(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "config.json").write_text("{}", encoding="utf-8")
    policy_path = _write_policy(tmp_path, [_rule("config", "config.json")])
    inventory_path = tmp_path / "config" / "content_inventory.json"
    inventory_path.write_text("original\n", encoding="utf-8")

    def reject_replace(source: Path, target: Path) -> None:
        raise OSError(f"replace rejected: {source.name} -> {target.name}")

    monkeypatch.setattr(inventory_module.os, "replace", reject_replace)

    with pytest.raises(OSError, match="replace rejected"):
        write_inventory(tmp_path, policy_path, inventory_path)

    assert inventory_path.read_text(encoding="utf-8") == "original\n"
    assert list(inventory_path.parent.glob(f".{inventory_path.name}.*.tmp")) == []


def test_check_reports_missing_inventory(tmp_path: Path) -> None:
    (tmp_path / "assets").mkdir()
    (tmp_path / "assets" / "file.txt").write_text("value", encoding="utf-8")
    policy_path = _write_policy(tmp_path, [_rule("text", "file.txt")])

    with pytest.raises(ContentInventoryError, match="does not exist"):
        check_inventory(tmp_path, policy_path, tmp_path / "missing.json")


@pytest.mark.parametrize(
    ("change", "message"),
    [
        (lambda policy: policy.update(schemaVersion=2), "unsupported content policy schema"),
        (lambda policy: policy.update(extra=True), "invalid fields"),
        (lambda policy: policy.update(rules=[]), "non-empty array"),
        (lambda policy: policy.update(assetRoot="../assets"), "unsafe path segment"),
        (lambda policy: policy["rules"][0].update(runtimeUse="sometimes"), "invalid runtimeUse"),
        (lambda policy: policy["rules"][0].update(shipStatus="maybe"), "invalid shipStatus"),
        (
            lambda policy: policy["rules"][0]["rights"].update(status="unknown"),
            "invalid rights status",
        ),
        (
            lambda policy: policy["rules"][0].update(shipStatus="approved", rights=deepcopy(UNVERIFIED_RIGHTS)),
            "without cleared rights",
        ),
        (
            lambda policy: policy["rules"][0].update(runtimeUse="required", shipStatus="excluded"),
            "cannot exclude",
        ),
    ],
)
def test_policy_rejects_invalid_contracts(tmp_path: Path, change, message: str) -> None:
    policy = {
        "schemaVersion": 1,
        "assetRoot": "assets",
        "rules": [_rule("files", "*.txt")],
    }
    change(policy)
    policy_path = tmp_path / "policy.json"
    policy_path.write_text(json.dumps(policy), encoding="utf-8")

    with pytest.raises(ContentInventoryError, match=message):
        load_policy(policy_path)


def test_policy_rejects_duplicate_rule_ids(tmp_path: Path) -> None:
    policy_path = _write_policy(
        tmp_path,
        [_rule("duplicate", "a.txt"), _rule("duplicate", "b.txt")],
    )
    with pytest.raises(ContentInventoryError, match="duplicate content policy rule id"):
        load_policy(policy_path)


def test_policy_rejects_unreadable_json(tmp_path: Path) -> None:
    policy_path = tmp_path / "policy.json"
    policy_path.write_text("not json", encoding="utf-8")
    with pytest.raises(ContentInventoryError, match="unreadable"):
        load_policy(policy_path)


def test_build_rejects_unmatched_ambiguous_and_unused_rules(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "file.txt").write_text("value", encoding="utf-8")

    unmatched = _write_policy(tmp_path, [_rule("json", "*.json")])
    with pytest.raises(ContentInventoryError, match="no content policy rule"):
        build_inventory(tmp_path, unmatched)

    ambiguous = _write_policy(
        tmp_path,
        [_rule("all", "*.txt"), _rule("exact", "file.txt")],
    )
    with pytest.raises(ContentInventoryError, match="multiple content policy rules"):
        build_inventory(tmp_path, ambiguous)

    unused = _write_policy(
        tmp_path,
        [_rule("exact", "file.txt"), _rule("unused", "other.txt")],
    )
    with pytest.raises(ContentInventoryError, match="match no assets"):
        build_inventory(tmp_path, unused)


def test_build_rejects_unsupported_media_and_empty_approved_asset(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "file.bin").write_bytes(b"value")
    policy_path = _write_policy(tmp_path, [_rule("binary", "file.bin")])
    with pytest.raises(ContentInventoryError, match="unsupported media extension"):
        build_inventory(tmp_path, policy_path)

    (assets / "file.bin").unlink()
    (assets / "empty.txt").write_bytes(b"")
    policy_path = _write_policy(tmp_path, [_rule("empty", "empty.txt")])
    with pytest.raises(ContentInventoryError, match="failed integrity validation"):
        build_inventory(tmp_path, policy_path)


def test_blocked_runtime_integrity_and_rights_are_release_blockers(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "empty.mp3").write_bytes(b"")
    policy_path = _write_policy(
        tmp_path,
        [
            _rule(
                "blocked",
                "empty.mp3",
                ship_status="blocked",
                rights=UNVERIFIED_RIGHTS,
            )
        ],
    )

    inventory = build_inventory(tmp_path, policy_path)

    assert inventory["assets"][0]["integrityStatus"] == "empty"
    assert release_blockers(inventory) == [
        "empty.mp3: runtime asset is blocked for shipping",
        "empty.mp3: runtime asset integrity is empty",
    ]


def test_basic_png_wav_mp3_and_utf8_structure_checks(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "image.png").write_bytes(_png())
    wav = (
        b"RIFF"
        + struct.pack("<I", 38)
        + b"WAVE"
        + b"fmt "
        + struct.pack("<IHHIIHH", 16, 1, 1, 8000, 8000, 1, 8)
        + b"data"
        + struct.pack("<I", 1)
        + b"\x80"
    )
    (assets / "cue.wav").write_bytes(wav)
    (assets / "cue.mp3").write_bytes(_mp3_frame() * 2)
    (assets / "notes.txt").write_text("valid", encoding="utf-8")
    policy_path = _write_policy(
        tmp_path,
        [
            _rule("png", "image.png"),
            _rule("wav", "cue.wav"),
            _rule("mp3", "cue.mp3"),
            _rule("text", "notes.txt"),
        ],
    )

    inventory = build_inventory(tmp_path, policy_path)

    assert inventory["summary"]["byIntegrityStatus"] == {"valid": 4}
    assert release_blockers(inventory) == []


def test_indexed_png_requires_a_valid_palette_and_decodable_scanlines(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "valid.png").write_bytes(_indexed_png())
    policy_path = _write_policy(tmp_path, [_rule("png", "valid.png")])

    inventory = build_inventory(tmp_path, policy_path)

    assert inventory["assets"][0]["integrityStatus"] == "valid"


@pytest.mark.parametrize(
    ("name", "payload", "detail"),
    [
        ("truncated.png", b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR", "truncated"),
        ("bad-crc.png", _png()[:-1] + b"\x00", "CRC"),
        ("giant.png", _png(width=65_536, height=1), "dimension"),
        ("missing-palette.png", _indexed_png(include_palette=False), "requires PLTE"),
        ("invalid-zlib.png", _indexed_png(image_data=b"not-zlib"), "zlib"),
        (
            "invalid-filter.png",
            _indexed_png(image_data=zlib.compress(b"\x05\x00")),
            "filter method",
        ),
        (
            "extra-scanline.png",
            _indexed_png(image_data=zlib.compress(b"\x00\x00\x00")),
            "exceeds",
        ),
        ("one-frame.mp3", _mp3_frame(), "consecutive"),
        ("truncated-frame.mp3", _mp3_frame()[:32], "complete"),
    ],
)
def test_media_integrity_rejects_incomplete_or_allocation_amplifying_files(
    tmp_path: Path,
    name: str,
    payload: bytes,
    detail: str,
) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / name).write_bytes(payload)
    policy_path = _write_policy(
        tmp_path,
        [
            _rule(
                "blocked-media",
                name,
                runtime_use="none",
                ship_status="excluded",
                rights={
                    "status": "not-applicable",
                    "source": "test fixture",
                    "license": "NOT_FOR_DISTRIBUTION",
                    "attribution": "none",
                    "reviewNote": "invalid on purpose",
                },
            )
        ],
    )

    inventory = build_inventory(tmp_path, policy_path)

    entry = inventory["assets"][0]
    assert entry["integrityStatus"] == "invalid"
    assert detail.casefold() in entry["integrityDetail"].casefold()


def test_invalid_structures_are_recorded_when_shipping_is_blocked(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "bad.json").write_text("{", encoding="utf-8")
    (assets / "bad.png").write_bytes(b"not a png")
    (assets / "bad.wav").write_bytes(b"not a wav")
    (assets / "bad.mp3").write_bytes(b"not an mp3")
    rules = [
        _rule(
            name,
            f"bad.{extension}",
            runtime_use="none",
            ship_status="excluded",
            rights={
                "status": "not-applicable",
                "source": "test fixture",
                "license": "NOT_FOR_DISTRIBUTION",
                "attribution": "none",
                "reviewNote": "invalid on purpose",
            },
        )
        for name, extension in (("json", "json"), ("png", "png"), ("wav", "wav"), ("mp3", "mp3"))
    ]
    policy_path = _write_policy(tmp_path, rules)

    inventory = build_inventory(tmp_path, policy_path)

    assert inventory["summary"]["byIntegrityStatus"] == {"invalid": 4}
    assert release_blockers(inventory) == []


def test_policy_and_inventory_must_stay_inside_repository(tmp_path: Path) -> None:
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "file.txt").write_text("value", encoding="utf-8")
    outside = tmp_path.parent / f"{tmp_path.name}-outside-policy.json"
    outside.write_text(
        json.dumps({"schemaVersion": 1, "assetRoot": "assets", "rules": [_rule("text", "file.txt")]}),
        encoding="utf-8",
    )
    try:
        with pytest.raises(ContentInventoryError, match="inside the repository"):
            build_inventory(tmp_path, outside)
    finally:
        outside.unlink(missing_ok=True)
