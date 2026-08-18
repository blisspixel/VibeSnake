"""Validate relative links in the canonical Markdown documentation."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[1]
LINK_PATTERN = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
SCHEMES = {"http", "https", "mailto", "tel", "data"}
ROOT_DOCUMENTS = (
    "README.md",
    "ROADMAP.md",
    "CHANGELOG.md",
    "CODE_OF_CONDUCT.md",
    "CONTRIBUTING.md",
    "SECURITY.md",
    "SUPPORT.md",
)
SUPPORTING_DOCUMENTS = (
    Path("assets/README.md"),
    Path("assets/ai/README.md"),
    Path("config/README.md"),
    Path("data/README.md"),
    Path("native/README.md"),
    Path("scripts/README.md"),
    Path("scripts/manual/README.md"),
    Path("tests/README.md"),
    Path("docs/research/README.md"),
)
NONCANONICAL_DOC_TREES = {"research"}


def canonical_documents() -> list[Path]:
    """Return current docs while intentionally excluding research pointers."""
    documents = [ROOT / name for name in ROOT_DOCUMENTS]
    documents.extend(
        path
        for path in sorted((ROOT / "docs").rglob("*.md"))
        if not NONCANONICAL_DOC_TREES.intersection(path.relative_to(ROOT / "docs").parts)
    )
    documents.extend(ROOT / path for path in SUPPORTING_DOCUMENTS)
    return list(dict.fromkeys(documents))


def link_targets(document: Path):
    """Yield line numbers and targets outside fenced code blocks."""
    in_fence = False
    for line_number, line in enumerate(document.read_text(encoding="utf-8").splitlines(), start=1):
        if line.lstrip().startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for match in LINK_PATTERN.finditer(line):
            yield line_number, match.group(1).strip()


def local_path(document: Path, target: str) -> Path | None:
    """Resolve a Markdown target to a local path, or return None for non-file links."""
    if target.startswith("#"):
        return None
    if target.startswith("<") and target.endswith(">"):
        target = target[1:-1]

    parsed = urlsplit(target)
    if parsed.scheme.lower() in SCHEMES or parsed.netloc:
        return None

    path_text = unquote(parsed.path)
    if not path_text:
        return None
    if path_text.startswith("/"):
        return ROOT / path_text.lstrip("/")
    return document.parent / path_text


CHANGELOG = ROOT / "CHANGELOG.md"
CONTRACT_RELEASE = re.compile(
    r"contracts to `(?P<version>\d+\.\d+\.\d+)` with rules resource (?P<resource>v\d+)"
)


def changelog_contract_failures() -> list[str]:
    """Report changelog entries that claim the same contract release twice.

    Each entry names the release it shipped in, so two entries claiming one
    version means an edit rewrote a past entry rather than adding a new one.
    A blanket version replace across the tree does exactly that, and it is
    invisible in review because every individual line still reads correctly.
    """
    if not CHANGELOG.is_file():
        return ["missing CHANGELOG.md"]

    failures: list[str] = []
    seen_versions: dict[str, int] = {}
    seen_resources: dict[str, int] = {}
    for line_number, line in enumerate(CHANGELOG.read_text(encoding="utf-8").splitlines(), 1):
        match = CONTRACT_RELEASE.search(line)
        if match is None:
            continue
        version = match.group("version")
        resource = match.group("resource")
        if version in seen_versions:
            failures.append(
                f"CHANGELOG.md:{line_number}: agent contract version {version} is already "
                f"claimed on line {seen_versions[version]}; each entry names its own release"
            )
        if resource in seen_resources:
            failures.append(
                f"CHANGELOG.md:{line_number}: rules resource {resource} is already claimed "
                f"on line {seen_resources[resource]}; each entry names its own resource"
            )
        seen_versions.setdefault(version, line_number)
        seen_resources.setdefault(resource, line_number)

    return failures


def main() -> int:
    """Report missing relative link targets and return a process status."""
    failures = []
    documents = canonical_documents()

    for document in documents:
        if not document.is_file():
            failures.append(f"missing canonical document: {document.relative_to(ROOT)}")
            continue
        for line_number, target in link_targets(document):
            path = local_path(document, target)
            if path is not None and not path.exists():
                failures.append(f"{document.relative_to(ROOT)}:{line_number}: missing target {target}")

    failures.extend(changelog_contract_failures())

    if failures:
        print("Documentation link check failed:")
        for failure in failures:
            print(f"  {failure}")
        return 1

    print(f"Documentation link check passed for {len(documents)} canonical files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
