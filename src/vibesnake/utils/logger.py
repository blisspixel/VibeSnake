"""Configure package-scoped console logging and optional file output."""

from collections.abc import Iterator
from contextlib import contextmanager
import logging
from pathlib import Path
import sys


def setup_logger(
    name: str = "vibesnake",
    level: int = logging.INFO,
    log_file: Path | None = None,
) -> logging.Logger:
    """Configure one logger unless it already owns direct handlers.

    The console handler writes messages at ``level`` to standard output. When a
    file is provided, a second handler records DEBUG and higher messages with a
    timestamp. The caller owns the file path and its parent directory.
    """
    logger = logging.getLogger(name)
    if logger.handlers:
        return logger

    logger.setLevel(level)

    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(level)
    console_handler.setFormatter(logging.Formatter("[%(levelname)s] %(name)s: %(message)s"))
    logger.addHandler(console_handler)

    if log_file is not None:
        file_handler = logging.FileHandler(log_file)
        file_handler.setLevel(logging.DEBUG)
        file_handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] %(name)s: %(message)s"))
        logger.addHandler(file_handler)

    return logger


default_logger = setup_logger()


def get_logger(name: str) -> logging.Logger:
    """Return a child of ``vibesnake`` without duplicating that prefix."""
    qualified_name = name if name == "vibesnake" or name.startswith("vibesnake.") else f"vibesnake.{name}"
    return logging.getLogger(qualified_name)


@contextmanager
def temporary_logger_level(name: str, level: int) -> Iterator[None]:
    """Set a logger level for one operation and restore its exact prior level."""
    logger = logging.getLogger(name)
    previous_level = logger.level
    logger.setLevel(level)
    try:
        yield
    finally:
        logger.setLevel(previous_level)
