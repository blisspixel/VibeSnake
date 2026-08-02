"""Logging namespace contracts."""

from vibesnake.utils.logger import get_logger


def test_get_logger_uses_exactly_one_package_prefix() -> None:
    assert get_logger("vibesnake.core.sample").name == "vibesnake.core.sample"
    assert get_logger("core.sample").name == "vibesnake.core.sample"
    assert get_logger("vibesnake").name == "vibesnake"
