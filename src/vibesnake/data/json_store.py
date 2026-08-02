"""Small, durable JSON persistence primitives for local save repositories."""

import json
import os
import shutil
import tempfile
from pathlib import Path
from typing import Any, Optional


class UnsupportedSchemaVersionError(ValueError):
    """Raised when a save was written by a newer, incompatible game version."""


def atomic_write_json(path: Path, payload: Any) -> None:
    """Write JSON through a same-directory temporary file and atomic replace."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=path.parent,
        prefix=f".{path.name}.",
        suffix=".tmp",
    )
    temporary_path = Path(temporary_name)

    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(payload, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)


def backup_corrupt_file(path: Path) -> Optional[Path]:
    """Preserve the latest unreadable bytes in one atomic recovery file."""
    path = Path(path)
    if not path.is_file():
        return None

    candidate = path.with_name(f"{path.name}.corrupt.bak")
    try:
        if candidate.is_file() and not candidate.is_symlink() and _files_have_equal_contents(path, candidate):
            return candidate

        descriptor, temporary_name = tempfile.mkstemp(
            dir=path.parent,
            prefix=f".{candidate.name}.",
            suffix=".tmp",
        )
        os.close(descriptor)
        temporary_path = Path(temporary_name)
        try:
            shutil.copy2(path, temporary_path)
            os.replace(temporary_path, candidate)
        finally:
            temporary_path.unlink(missing_ok=True)
        return candidate
    except OSError:
        return None


def _files_have_equal_contents(left: Path, right: Path) -> bool:
    if left.stat().st_size != right.stat().st_size:
        return False

    with left.open("rb") as left_stream, right.open("rb") as right_stream:
        while True:
            left_chunk = left_stream.read(64 * 1024)
            right_chunk = right_stream.read(64 * 1024)
            if left_chunk != right_chunk:
                return False
            if not left_chunk:
                return True
