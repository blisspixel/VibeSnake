"""Contracts for repository-script checkout isolation."""

import sys

from scripts._checkout import promote_checkout_source


def test_checkout_source_is_promoted_ahead_of_installed_packages(tmp_path, monkeypatch):
    source_root = tmp_path / "src"
    source_root.mkdir()
    source_text = str(source_root.resolve())
    monkeypatch.setattr(sys, "path", ["installed", source_text, "later", source_text])

    resolved = promote_checkout_source(tmp_path)

    assert resolved == source_root.resolve()
    assert sys.path[0] == source_text
    assert sys.path.count(source_text) == 1
