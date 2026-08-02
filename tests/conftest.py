"""Shared pytest configuration for deterministic, headless tests."""

import os
import random
import shutil
import tempfile
from pathlib import Path

import pytest


os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

_TEST_DATA_DIR = Path(tempfile.mkdtemp(prefix=f"vibesnake-tests-{os.getpid()}-"))
os.environ["VIBESNAKE_DATA_DIR"] = str(_TEST_DATA_DIR)


@pytest.fixture(autouse=True)
def isolated_persistent_data():
    """Start every test with empty saves and the same reference random stream."""
    random.seed(0)
    _TEST_DATA_DIR.mkdir(parents=True, exist_ok=True)
    for path in _TEST_DATA_DIR.glob("*.json"):
        path.unlink()
    yield


@pytest.fixture(scope="session", autouse=True)
def clean_test_data_dir():
    """Remove the temporary save directory after the test session."""
    yield
    shutil.rmtree(_TEST_DATA_DIR, ignore_errors=True)
