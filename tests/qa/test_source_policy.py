from pathlib import Path

from vibesnake.qa.source_policy import inspect_repository, policy_files


def test_policy_files_exclude_historical_and_generated_trees(tmp_path: Path) -> None:
    active = tmp_path / "src" / "active.py"
    active.parent.mkdir()
    active.write_text("value = 1\n", encoding="utf-8")
    archived = tmp_path / "docs" / "archive" / "history.md"
    archived.parent.mkdir(parents=True)
    archived.write_text("historical\n", encoding="utf-8")
    generated = tmp_path / "native" / "obj" / "generated.cs"
    generated.parent.mkdir(parents=True)
    generated.write_text("generated\n", encoding="utf-8")

    assert policy_files(tmp_path) == (active,)


def test_policy_reports_text_and_python_placeholders(tmp_path: Path) -> None:
    marker = "TO" + "DO"
    em_dash = chr(0x2014)
    emoji = chr(0x1F40D)
    bad_source = tmp_path / "src" / "bad.py"
    bad_source.parent.mkdir()
    bad_source.write_text(
        "\n".join(
            (
                f"# {marker}",
                "def unfinished():",
                "    pass",
                "assert True",
                "try:",
                "    value = 1",
                "except:",
                "    value = 2",
                "...",
                f'label = "bad{em_dash}separator"',
                f'icon = "{emoji}"',
                "",
            )
        ),
        encoding="utf-8",
    )

    messages = [violation.message for violation in inspect_repository(tmp_path)]

    assert messages == [
        "unfinished-work marker is forbidden",
        "empty pass statement is forbidden",
        "constant-true assertion is forbidden",
        "bare except clause is forbidden",
        "ellipsis placeholder is forbidden",
        "em dash is forbidden",
        "emoji is forbidden",
    ]


def test_policy_rejects_unfinished_markers_regardless_of_case(tmp_path: Path) -> None:
    source = tmp_path / "src" / "unfinished.py"
    source.parent.mkdir()
    source.write_text("".join(("# to", "do: finish this\n")), encoding="utf-8")

    messages = [violation.message for violation in inspect_repository(tmp_path)]

    assert messages == ["unfinished-work marker is forbidden"]


def test_policy_accepts_explicit_complete_code(tmp_path: Path) -> None:
    source = tmp_path / "src" / "complete.py"
    source.parent.mkdir()
    source.write_text(
        "def reciprocal(value: float) -> float:\n"
        "    if value == 0:\n"
        "        raise ValueError('value must be nonzero')\n"
        "    return 1 / value\n",
        encoding="utf-8",
    )

    assert inspect_repository(tmp_path) == ()


def test_policy_rejects_signing_and_credential_material(tmp_path: Path) -> None:
    credential_paths = (
        tmp_path / "config" / ".env.production",
        tmp_path / "signing" / "windows.p12",
        tmp_path / "signing" / "AuthKey_RELEASE.p8",
        tmp_path / "signing" / "release.keystore",
    )
    for path in credential_paths:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("private", encoding="utf-8")

    violations = inspect_repository(tmp_path)

    assert len(violations) == len(credential_paths)
    assert {violation.path for violation in violations} == {path.relative_to(tmp_path) for path in credential_paths}
    assert {violation.message for violation in violations} == {"credential or signing material is forbidden"}
