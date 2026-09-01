"""Close the current Agent Arena preview slice from one command.

Patches public-contract digests, regenerates knowledge, checks interop and
docs, then runs the focused native tests. Does not push. Optional --commit
creates a local commit only after those gates pass.
"""

from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parent
ROOT = SCRIPTS.parent
TEST_PROJECT = ROOT / "native" / "tests" / "VibeSnake.Rules.Tests" / "VibeSnake.Rules.Tests.csproj"
REPOSITORY_CHECKS = ROOT / "native" / "tools" / "RepositoryChecks" / "RepositoryChecks.csproj"
JUNK = (
    ROOT / "_aa07_runner.cmd",
    ROOT / "_aa07_nul_test.txt",
    ROOT / "_aa07_nul_test.bin",
    ROOT / ".envrc",
)


def _repo_dotnet() -> Path:
    candidate = ROOT / ".dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if candidate.is_file():
        return candidate
    return Path("dotnet")


def _run(command: list[str], *, env: dict[str, str]) -> int:
    print("+ " + " ".join(command), flush=True)
    completed = subprocess.run(command, cwd=ROOT, env=env, check=False)
    return completed.returncode


def _clean_junk() -> None:
    for path in JUNK:
        if path.is_file():
            path.unlink()
            print(f"removed {path.name}", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--full",
        action="store_true",
        help="Also run scripts/test_native.ps1 after the focused Agent Arena tests.",
    )
    parser.add_argument(
        "--commit",
        action="store_true",
        help="Create a local git commit if every gate in this script passes. Does not push.",
    )
    arguments = parser.parse_args()

    env = os.environ.copy()
    repo_dotnet = ROOT / ".dotnet"
    if repo_dotnet.is_dir():
        env["DOTNET_ROOT"] = str(repo_dotnet)
        env["DOTNET_ROOT_X64"] = str(repo_dotnet)
        env["PATH"] = str(repo_dotnet) + os.pathsep + env.get("PATH", "")

    print("1. public-contract digests", flush=True)
    if _run(
        [
            str(_repo_dotnet()),
            "run",
            "--project",
            str(REPOSITORY_CHECKS),
            "--configuration",
            "Release",
            "--",
            "interop-write",
            str(ROOT),
        ],
        env=env,
    ):
        return 1

    print("2. agent knowledge", flush=True)
    if _run(
        [
            str(_repo_dotnet()),
            "run",
            "--project",
            str(REPOSITORY_CHECKS),
            "--configuration",
            "Release",
            "--",
            "knowledge-write",
            str(ROOT),
        ],
        env=env,
    ):
        return 1

    print("3. interoperability baseline", flush=True)
    if _run(
        [
            str(_repo_dotnet()),
            "run",
            "--project",
            str(REPOSITORY_CHECKS),
            "--configuration",
            "Release",
            "--",
            "interop",
            str(ROOT),
        ],
        env=env,
    ):
        return 1

    print("4. documentation links", flush=True)
    if _run(
        [
            str(_repo_dotnet()),
            "run",
            "--project",
            str(REPOSITORY_CHECKS),
            "--",
            "docs",
            str(ROOT),
        ],
        env=env,
    ):
        return 1

    print("5. focused native tests", flush=True)
    test_filter = (
        "FullyQualifiedName~AgentPassport|"
        "FullyQualifiedName~AgentExhibitionStory|"
        "FullyQualifiedName~AgentQualification|"
        "FullyQualifiedName~AgentHostTests"
    )
    if _run(
        [
            str(_repo_dotnet()),
            "test",
            str(TEST_PROJECT),
            "--filter",
            test_filter,
            "--nologo",
        ],
        env=env,
    ):
        return 1

    if arguments.full:
        print("6. full native quality loop", flush=True)
        pwsh = "pwsh"
        if _run(
            [pwsh, "-NoProfile", "-File", str(SCRIPTS / "test_native.ps1")],
            env=env,
        ):
            return 1

    _clean_junk()
    print("Agent Arena preview close-out passed.", flush=True)
    if arguments.commit:
        print("7. local commit", flush=True)
        staged = _run(["git", "add", "-A"], env=env)
        if staged:
            return staged
        message = "Keep a local public record of verified agent exhibitions"
        return _run(["git", "commit", "-m", message], env=env)

    print(
        "Review the diff, then: git add -A && git commit && git push origin main",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
