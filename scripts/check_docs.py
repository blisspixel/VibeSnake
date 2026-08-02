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

    if failures:
        print("Documentation link check failed:")
        for failure in failures:
            print(f"  {failure}")
        return 1

    print(f"Documentation link check passed for {len(documents)} canonical files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
