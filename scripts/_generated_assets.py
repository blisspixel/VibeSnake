"""Shared persistence for deterministic generated visual assets."""

from __future__ import annotations

import os
from pathlib import Path
from tempfile import mkstemp


def write_atomic(path: Path, payload: bytes) -> None:
    """Replace ``path`` with fully flushed bytes from the same filesystem."""
    descriptor, temporary_name = mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    temporary_path = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)
