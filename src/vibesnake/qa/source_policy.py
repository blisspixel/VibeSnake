"""Enforce repository rules that should never depend on reviewer memory."""

from __future__ import annotations

import ast
from dataclasses import dataclass
from pathlib import Path
import re
from typing import Iterable


_SCAN_ROOTS = (".github", "docs", "game", "native", "scripts", "src", "tests")
_ROOT_FILES = (
    ".gitattributes",
    ".gitignore",
    ".pre-commit-config.yaml",
    "CHANGELOG.md",
    "CODE_OF_CONDUCT.md",
    "CONTRIBUTING.md",
    "Directory.Build.props",
    "LICENSE",
    "NOTICE",
    "pyproject.toml",
    "README.md",
    "ROADMAP.md",
    "SECURITY.md",
    "SUPPORT.md",
)
_SUPPORTING_FILES = (
    "assets/README.md",
    "config/README.md",
    "data/README.md",
)
_TEXT_SUFFIXES = {
    ".cfg",
    ".cs",
    ".csproj",
    ".gd",
    ".godot",
    ".ini",
    ".md",
    ".props",
    ".ps1",
    ".py",
    ".slnx",
    ".toml",
    ".tscn",
    ".tres",
    ".xml",
    ".yaml",
    ".yml",
}
_EXCLUDED_PARTS = {
    ".agent",
    ".git",
    ".godot",
    ".mypy_cache",
    ".pytest_cache",
    ".ruff_cache",
    "__pycache__",
    "bin",
    "obj",
}
_MARKER_EXEMPTIONS = {Path("docs/engineering/CODE_QUALITY_STANDARDS.md")}
_FORBIDDEN_MARKERS = tuple("".join(parts) for parts in (("TO", "DO"), ("FIX", "ME"), ("HA", "CK"), ("X", "XX")))
_MARKER_PATTERN = re.compile(
    rf"\b(?:{'|'.join(re.escape(marker) for marker in _FORBIDDEN_MARKERS)})\b",
    re.IGNORECASE,
)


@dataclass(frozen=True, order=True)
class PolicyViolation:
    """One actionable source-policy failure."""

    path: Path
    line: int
    message: str

    def render(self) -> str:
        """Return a stable diagnostic suitable for local tools and CI."""
        return f"{self.path.as_posix()}:{self.line}: {self.message}"


def _is_emoji(character: str) -> bool:
    codepoint = ord(character)
    return 0x1F1E6 <= codepoint <= 0x1F1FF or 0x1F300 <= codepoint <= 0x1FAFF or codepoint in {0x200D, 0x20E3, 0xFE0F}


def _is_excluded(relative_path: Path) -> bool:
    parts = set(relative_path.parts)
    if parts.intersection(_EXCLUDED_PARTS):
        return True
    return len(relative_path.parts) >= 2 and relative_path.parts[:2] in {
        ("docs", "archive"),
        ("docs", "research"),
    }


def policy_files(repository_root: Path) -> tuple[Path, ...]:
    """Return the deterministic set of active, authored text files."""
    root = repository_root.resolve()
    candidates: set[Path] = set()
    for name in (*_ROOT_FILES, *_SUPPORTING_FILES):
        path = root / name
        if path.is_file():
            candidates.add(path)

    for directory_name in _SCAN_ROOTS:
        directory = root / directory_name
        if not directory.is_dir():
            continue
        for path in directory.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in _TEXT_SUFFIXES:
                continue
            relative_path = path.relative_to(root)
            if not _is_excluded(relative_path):
                candidates.add(path)
    return tuple(sorted(candidates))


def _text_violations(relative_path: Path, text: str) -> Iterable[PolicyViolation]:
    for line_number, line in enumerate(text.splitlines(), start=1):
        if "\N{EM DASH}" in line:
            yield PolicyViolation(relative_path, line_number, "em dash is forbidden")
        if any(_is_emoji(character) for character in line):
            yield PolicyViolation(relative_path, line_number, "emoji is forbidden")
        if relative_path not in _MARKER_EXEMPTIONS and _MARKER_PATTERN.search(line):
            yield PolicyViolation(relative_path, line_number, "unfinished-work marker is forbidden")


def _python_violations(relative_path: Path, text: str) -> Iterable[PolicyViolation]:
    try:
        tree = ast.parse(text, filename=relative_path.as_posix())
    except SyntaxError as error:
        yield PolicyViolation(relative_path, error.lineno or 1, f"invalid Python syntax: {error.msg}")
        return

    for node in ast.walk(tree):
        if isinstance(node, ast.Pass):
            yield PolicyViolation(relative_path, node.lineno, "empty pass statement is forbidden")
        elif isinstance(node, ast.Assert) and isinstance(node.test, ast.Constant) and node.test.value is True:
            yield PolicyViolation(relative_path, node.lineno, "constant-true assertion is forbidden")
        elif isinstance(node, ast.ExceptHandler) and node.type is None:
            yield PolicyViolation(relative_path, node.lineno, "bare except clause is forbidden")
        elif isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and node.value.value is Ellipsis:
            yield PolicyViolation(relative_path, node.lineno, "ellipsis placeholder is forbidden")


def inspect_repository(repository_root: Path) -> tuple[PolicyViolation, ...]:
    """Inspect active source and canonical docs without mutating the repository."""
    root = repository_root.resolve()
    violations: list[PolicyViolation] = []
    for path in policy_files(root):
        relative_path = path.relative_to(root)
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            violations.append(PolicyViolation(relative_path, 1, f"unreadable UTF-8 text: {error}"))
            continue
        violations.extend(_text_violations(relative_path, text))
        if path.suffix.lower() == ".py":
            violations.extend(_python_violations(relative_path, text))
    return tuple(sorted(set(violations)))
