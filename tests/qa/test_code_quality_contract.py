from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

import yaml


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
FULL_COMMIT_SHA = re.compile(r"^[0-9a-f]{40}$")
RELEASE_TAG = re.compile(r"^v\d+(?:\.\d+){1,2}$")
ACTION_REFERENCE = re.compile(r"^\s*(?:-\s*)?uses:\s*([^\s@]+)@([^\s#]+)\s*(?:#\s*(\S+))?\s*$")


def workflow_paths() -> list[Path]:
    workflows = REPOSITORY_ROOT / ".github" / "workflows"
    return sorted((*workflows.glob("*.yml"), *workflows.glob("*.yaml")))


def load_workflow(path: Path) -> dict[str, object]:
    loaded = yaml.load(path.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
    assert isinstance(loaded, dict), path
    return loaded


def test_dotnet_quality_contract_is_explicit_and_stable() -> None:
    root = ET.parse(REPOSITORY_ROOT / "Directory.Build.props").getroot()
    properties = {child.tag: (child.text or "").strip() for group in root.findall("PropertyGroup") for child in group}

    assert properties["LangVersion"] == "14.0"
    assert properties["Nullable"] == "enable"
    assert properties["AnalysisLevel"] == "10.0-recommended"
    assert properties["EnforceCodeStyleInBuild"] == "true"
    assert properties["TreatWarningsAsErrors"] == "true"
    assert properties["Deterministic"] == "true"
    assert properties["RestorePackagesWithLockFile"] == "true"
    assert properties["NuGetAudit"] == "true"
    assert properties["NuGetAuditMode"] == "all"
    assert properties["NuGetAuditLevel"] == "low"

    global_config = json.loads((REPOSITORY_ROOT / "global.json").read_text(encoding="utf-8"))
    toolchain = json.loads((REPOSITORY_ROOT / "native" / "toolchain.json").read_text(encoding="utf-8"))
    assert global_config["sdk"]["version"] == "10.0.303"
    assert global_config["sdk"]["rollForward"] == "disable"
    assert global_config["sdk"]["allowPrerelease"] is False
    assert toolchain["dotnetSdk"]["version"] == global_config["sdk"]["version"]

    workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
    assert workflow.count("dotnet-version: 10.0.303") == 3
    validator_installs = re.findall(
        r"python -m pip install --require-hashes --only-binary=:all:\s+-r requirements-ci\.lock",
        workflow,
    )
    assert len(validator_installs) == 2
    for script_name in ("write_dependency_inventory.ps1", "inspect_native_artifact.ps1"):
        script = (REPOSITORY_ROOT / "scripts" / script_name).read_text(encoding="utf-8")
        assert "dotnet --version" in script
        assert "does not match pinned SDK" in script

    inventory_script = (REPOSITORY_ROOT / "scripts" / "write_dependency_inventory.ps1").read_text(encoding="utf-8")
    committed_lock_paths = {
        path.relative_to(REPOSITORY_ROOT).as_posix()
        for path in REPOSITORY_ROOT.rglob("packages.lock.json")
        if not {".tools", "TestResults"}.intersection(path.relative_to(REPOSITORY_ROOT).parts)
    }
    assert len(committed_lock_paths) == 9
    for lock_path in committed_lock_paths:
        assert f'"{lock_path}"' in inventory_script

    host_project = ET.parse(
        REPOSITORY_ROOT / "native" / "tools" / "VibeSnake.AgentHost" / "VibeSnake.AgentHost.csproj"
    ).getroot()
    packages = {
        reference.attrib["Include"]: reference.attrib["Version"]
        for reference in host_project.findall(".//PackageReference")
    }
    assert packages["Microsoft.Extensions.Hosting"] == "10.0.11"
    assert packages["Microsoft.Extensions.Caching.Abstractions"] == "10.0.11"


def test_root_editorconfig_enforces_portable_text_and_csharp_formatting() -> None:
    editorconfig = (REPOSITORY_ROOT / ".editorconfig").read_text(encoding="utf-8")

    for required in (
        "root = true",
        "charset = utf-8",
        "end_of_line = lf",
        "insert_final_newline = true",
        "trim_trailing_whitespace = true",
        "csharp_prefer_braces = true:warning",
        "dotnet_diagnostic.IDE0055.severity = warning",
    ):
        assert required in editorconfig


def test_every_external_github_action_uses_a_full_commit_sha() -> None:
    references: list[tuple[Path, int, str, str, str | None]] = []
    malformed: list[str] = []
    for workflow in workflow_paths():
        for line_number, line in enumerate(
            workflow.read_text(encoding="utf-8").splitlines(),
            start=1,
        ):
            if re.match(r"^\s*(?:-\s*)?uses:", line) is None:
                continue
            match = ACTION_REFERENCE.fullmatch(line)
            if match is None:
                malformed.append(f"{workflow.relative_to(REPOSITORY_ROOT)}:{line_number} {line.strip()}")
                continue
            if not match.group(1).startswith(("./", "docker://")):
                references.append((workflow, line_number, match.group(1), match.group(2), match.group(3)))

    assert malformed == []
    assert references
    invalid_references = [
        f"{path.relative_to(REPOSITORY_ROOT)}:{line_number} {action}@{reference}"
        for path, line_number, action, reference, _tag in references
        if FULL_COMMIT_SHA.fullmatch(reference) is None
    ]
    invalid_tag_comments = [
        f"{path.relative_to(REPOSITORY_ROOT)}:{line_number} {action}@{reference}"
        for path, line_number, action, reference, tag in references
        if tag is None or RELEASE_TAG.fullmatch(tag) is None
    ]
    unapproved_actions = [
        f"{path.relative_to(REPOSITORY_ROOT)}:{line_number} {action}"
        for path, line_number, action, _reference, _tag in references
        if not action.startswith("actions/")
    ]
    assert invalid_references == []
    assert invalid_tag_comments == []
    assert unapproved_actions == []


def test_workflows_default_to_read_only_and_elevate_permissions_per_job() -> None:
    for path in workflow_paths():
        workflow = load_workflow(path)
        assert workflow.get("permissions") == {"contents": "read"}, path
        triggers = workflow.get("on")
        assert isinstance(triggers, dict), path
        assert "pull_request_target" not in triggers, path

        jobs = workflow.get("jobs")
        assert isinstance(jobs, dict), path
        for job_name, job in jobs.items():
            assert isinstance(job, dict), f"{path}:{job_name}"
            permissions = job.get("permissions")
            if permissions is None:
                continue
            assert isinstance(permissions, dict), f"{path}:{job_name}"
            assert set(permissions.values()) <= {"read", "write", "none"}, f"{path}:{job_name}"
            if "write" in permissions.values():
                steps = job.get("steps")
                assert isinstance(steps, list), f"{path}:{job_name}"
                action_names = [step.get("uses", "") for step in steps if isinstance(step, dict)]
                assert not any(action.startswith("actions/checkout@") for action in action_names), f"{path}:{job_name}"


def test_dependency_automation_is_bounded_and_covers_every_package_ecosystem() -> None:
    # This test covers committed ecosystem configuration. Live GitHub alert and
    # security-update settings are verified separately by the release checklist.
    config_path = REPOSITORY_ROOT / ".github" / "dependabot.yml"
    config = yaml.safe_load(config_path.read_text(encoding="utf-8"))

    assert config["version"] == 2
    updates = config["updates"]
    actual = {(entry["package-ecosystem"], entry["directory"]) for entry in updates}
    assert actual == {
        ("github-actions", "/"),
        ("nuget", "/game"),
        ("nuget", "/native"),
        ("pip", "/"),
    }
    for entry in updates:
        assert entry["schedule"]["interval"] == "monthly"
        assert entry["open-pull-requests-limit"] == 0


def test_ci_runs_the_documented_quality_and_dependency_gates() -> None:
    workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
    parsed = load_workflow(REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml")

    for required in (
        "python -m ruff check src tests scripts",
        "python -m ruff format --check src tests scripts",
        "python -m pytest --cov=vibesnake",
        "python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock",
        "python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock",
        "dotnet restore native/VibeSnake.slnx --locked-mode",
        "dotnet build native/VibeSnake.slnx --configuration Release --no-restore",
        "dotnet format native/VibeSnake.slnx --verify-no-changes --no-restore",
        "./scripts/test_native_coverage.ps1",
        "./scripts/package_agent_plugin.ps1 -OutputRoot TestResults/agent-plugin -Force",
        "VIBESNAKE_AGENT_HOST_ASSEMBLY:",
        "python scripts/check_agent_interop.py",
    ):
        assert required in workflow

    complete = parsed["jobs"]["ci-complete"]
    assert complete["name"] == "CI complete"
    assert complete["if"] == "always()"
    assert complete["needs"] == ["quality", "native-rules", "godot-smoke", "release-matrix"]
    assert complete["timeout-minutes"] == "2"
    assert '[[ "${result}" == "success" ]]' in workflow


def test_agent_interoperability_drift_workflow_is_bounded_and_required() -> None:
    path = REPOSITORY_ROOT / ".github" / "workflows" / "agent-interop-drift.yml"
    workflow = load_workflow(path)

    assert workflow["permissions"] == {"contents": "read"}
    assert workflow["on"] == {
        "schedule": [{"cron": "17 13 * * 1"}],
        "workflow_dispatch": "",
    }
    job = workflow["jobs"]["upstream-drift"]
    assert job["runs-on"] == "ubuntu-latest"
    assert job["timeout-minutes"] == "5"
    raw = path.read_text(encoding="utf-8")
    assert "python scripts/check_agent_interop.py --check-upstream" in raw


def test_floating_source_release_uses_only_a_successful_ci_revision() -> None:
    path = REPOSITORY_ROOT / ".github" / "workflows" / "player-build.yml"
    workflow = load_workflow(path)
    triggers = workflow["on"]
    assert isinstance(triggers, dict)
    assert "push" not in triggers
    assert "workflow_dispatch" not in triggers
    assert triggers["workflow_run"] == {
        "workflows": ["CI"],
        "types": ["completed"],
        "branches": ["main"],
    }

    raw = path.read_text(encoding="utf-8")
    assert "github.event.workflow_run.conclusion == 'success'" in raw
    assert "github.event.workflow_run.event == 'push'" in raw
    assert "github.event.workflow_run.head_repository.full_name == github.repository" in raw
    assert "ref: ${{ github.event.workflow_run.head_sha }}" in raw
    assert "python -m pip install --require-hashes --only-binary=:all: -r requirements-ci.lock" in raw
    assert "python -m build --no-isolation" in raw
    assert "**Native source player**" in raw
    assert ".\\\\play.ps1" in raw
    assert "./play.sh" in raw
    assert "**Frozen Python reference**" in raw
    assert "vibesnake update" not in raw
    assert '"repos/${GITHUB_REPOSITORY}/git/refs/tags/player-latest"' in raw
    assert '"repos/${GITHUB_REPOSITORY}/git/ref/tags/player-latest"' in raw
    assert '"repos/${GITHUB_REPOSITORY}/releases?per_page=100"' in raw
    assert '"repos/${GITHUB_REPOSITORY}/releases/tags/player-latest"' in raw
    assert '"repos/${GITHUB_REPOSITORY}/releases/${release_id}"' in raw
    assert "retry publish_player_release" in raw
    assert "if (( attempt >= 5 ))" in raw
    assert "-F force=true" in raw
    assert "[.object.type, .object.sha] | @tsv" in raw
    assert "$'commit\\t'\"${QUALIFIED_SHA}\"" in raw
    assert "[.tag_name, .draft, .prerelease] | @tsv" in raw
    assert "--verify-tag" in raw
    assert '--repo "${GITHUB_REPOSITORY}"' in raw
    assert "--cleanup-tag" not in raw
    assert '--target "${QUALIFIED_SHA}"' not in raw
    assert workflow["jobs"]["publish"]["permissions"] == {
        "actions": "read",
        "contents": "write",
    }
    assert "pip install --upgrade" not in raw
