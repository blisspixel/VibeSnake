"""Utilities that make repository scripts import the current checkout."""

from __future__ import annotations

from pathlib import Path
import sys


def promote_checkout_source(repository_root: Path) -> Path:
    """Place the checkout source first even when it already appears later."""
    source_root = (repository_root / "src").resolve()
    source_text = str(source_root)
    while source_text in sys.path:
        sys.path.remove(source_text)
    sys.path.insert(0, source_text)
    return source_root
