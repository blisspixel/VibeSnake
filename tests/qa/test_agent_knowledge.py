import shutil
import subprocess
import sys
from pathlib import Path

import yaml


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GENERATOR = REPOSITORY_ROOT / "scripts" / "generate_agent_knowledge.py"
KNOWLEDGE_ROOT = REPOSITORY_ROOT / "integrations" / "vibesnake-agent-knowledge"


def run_check(output: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            sys.executable,
            str(GENERATOR),
            "--check",
            "--repository-root",
            str(REPOSITORY_ROOT),
            "--output",
            str(output),
        ],
        check=False,
        capture_output=True,
        text=True,
    )


def frontmatter(path: Path) -> dict[str, object]:
    text = path.read_text(encoding="utf-8")
    assert text.startswith("---\n")
    _, raw, _ = text.split("---\n", maxsplit=2)
    parsed = yaml.safe_load(raw)
    assert isinstance(parsed, dict)
    return parsed


def test_checked_in_agent_knowledge_is_current_and_okf_02_conformant() -> None:
    result = run_check(KNOWLEDGE_ROOT)

    assert result.returncode == 0, result.stdout + result.stderr
    assert frontmatter(KNOWLEDGE_ROOT / "index.md") == {"okf_version": "0.2"}
    for path in sorted(KNOWLEDGE_ROOT.glob("*.md")):
        if path.name == "index.md":
            continue
        metadata = frontmatter(path)
        assert metadata["type"]
        assert metadata["status"] == "draft"
        assert metadata["generated"]["by"] == "process:vibesnake-okf-generator"
        assert metadata["verified"]["by"] == "process:vibesnake-ci"
        assert metadata["stale_after"] == "2026-11-13T00:00:00Z"
        sources = metadata["sources"]
        assert isinstance(sources, list) and sources
        for source in sources:
            assert "author" not in source
            resource = source["resource"]
            if resource.startswith("https://"):
                continue
            assert (KNOWLEDGE_ROOT / resource).resolve().exists()


def test_agent_knowledge_check_detects_drift_and_unexpected_concepts(tmp_path: Path) -> None:
    output = tmp_path / "knowledge"
    shutil.copytree(KNOWLEDGE_ROOT, output)
    (output / "rules.md").write_text("stale\n", encoding="utf-8")
    (output / "extra.md").write_text("---\ntype: Extra\n---\n", encoding="utf-8")

    result = run_check(output)

    assert result.returncode == 1
    assert "generated file is stale: rules.md" in result.stdout
    assert "unexpected generated concept: extra.md" in result.stdout


def test_agent_knowledge_check_reports_missing_files(tmp_path: Path) -> None:
    result = run_check(tmp_path)

    assert result.returncode == 1
    assert "missing generated file: index.md" in result.stdout
