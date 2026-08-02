"""Update a local Vibe Snake checkout from GitHub main."""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys

from vibesnake.checkout import DEFAULT_BRANCH, DEFAULT_REMOTE, find_checkout_root


class UpdateError(RuntimeError):
    """Raised when an update cannot complete safely."""


def _run(command: list[str], *, cwd: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=str(cwd),
        check=False,
        text=True,
        capture_output=True,
        encoding="utf-8",
        errors="replace",
    )


def _require_git(root: Path) -> None:
    probe = _run(["git", "--version"], cwd=root)
    if probe.returncode != 0:
        raise UpdateError("git is required for vibesnake update; install git and retry")


def _git(root: Path, *args: str) -> str:
    completed = _run(["git", *args], cwd=root)
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout or "").strip()
        raise UpdateError(detail or f"git {' '.join(args)} failed")
    return (completed.stdout or "").strip()


def update_checkout(
    root: Path | None = None,
    *,
    branch: str = DEFAULT_BRANCH,
    remote: str = "origin",
    reinstall: bool = True,
    dry_run: bool = False,
) -> dict[str, str]:
    """Fast-forward the checkout to the latest remote branch and optionally reinstall."""
    checkout = root or find_checkout_root()
    if checkout is None:
        raise UpdateError(
            f"could not find a Vibe Snake checkout; clone {DEFAULT_REMOTE} and run this command from that directory"
        )
    if not (checkout / ".git").exists():
        raise UpdateError(
            f"{checkout} is not a git checkout; reinstall with:\n"
            f'  python -m pip install --upgrade "git+{DEFAULT_REMOTE}@{branch}"'
        )

    _require_git(checkout)
    before = _git(checkout, "rev-parse", "--short", "HEAD")
    current_branch = _git(checkout, "rev-parse", "--abbrev-ref", "HEAD")
    status = _git(checkout, "status", "--porcelain")
    if status and not dry_run:
        raise UpdateError("checkout has local changes; commit, stash, or reset them before updating:\n" + status)

    if dry_run:
        return {
            "root": str(checkout),
            "branch": current_branch,
            "before": before,
            "after": before,
            "changed": "no",
            "mode": "dry-run",
        }

    _git(checkout, "fetch", remote, branch)
    _git(checkout, "pull", "--ff-only", remote, branch)
    after = _git(checkout, "rev-parse", "--short", "HEAD")

    if reinstall:
        _reinstall(checkout)

    return {
        "root": str(checkout),
        "branch": current_branch,
        "before": before,
        "after": after,
        "changed": "yes" if before != after else "no",
        "mode": "updated",
    }


def checkout_status(
    root: Path | None = None,
    *,
    branch: str = DEFAULT_BRANCH,
    remote: str = "origin",
) -> dict[str, str]:
    """Compare the local checkout to the remote branch without changing files."""
    checkout = root or find_checkout_root()
    if checkout is None:
        raise UpdateError(
            f"could not find a Vibe Snake checkout; clone {DEFAULT_REMOTE} and run this command from that directory"
        )
    if not (checkout / ".git").exists():
        raise UpdateError(f"{checkout} is not a git checkout")

    _require_git(checkout)
    local = _git(checkout, "rev-parse", "--short", "HEAD")
    current_branch = _git(checkout, "rev-parse", "--abbrev-ref", "HEAD")
    dirty = _git(checkout, "status", "--porcelain")
    fetch = _run(["git", "fetch", remote, branch], cwd=checkout)
    if fetch.returncode != 0:
        detail = (fetch.stderr or fetch.stdout or "").strip()
        raise UpdateError(detail or f"git fetch {remote} {branch} failed")
    remote_tip = _git(checkout, "rev-parse", "--short", f"{remote}/{branch}")
    ahead = _git(checkout, "rev-list", "--count", f"{remote}/{branch}..HEAD")
    behind = _git(checkout, "rev-list", "--count", f"HEAD..{remote}/{branch}")
    if behind != "0":
        state = "behind"
    elif ahead != "0":
        state = "ahead"
    else:
        state = "current"
    return {
        "root": str(checkout),
        "branch": current_branch,
        "local": local,
        "remote": remote_tip,
        "ahead": ahead,
        "behind": behind,
        "state": state,
        "dirty": "yes" if dirty else "no",
        "remote_ref": f"{remote}/{branch}",
    }


def _reinstall(checkout: Path) -> None:
    """Reinstall the editable package using the checkout lock file when present."""
    python = Path(sys.executable)
    lock = checkout / "requirements-runtime.lock"
    commands: list[list[str]] = []
    if lock.is_file():
        commands.append(
            [
                str(python),
                "-m",
                "pip",
                "install",
                "--require-hashes",
                "--only-binary=:all:",
                "-r",
                str(lock),
            ]
        )
    commands.append(
        [
            str(python),
            "-m",
            "pip",
            "install",
            "--no-deps",
            "--no-build-isolation",
            "-e",
            str(checkout),
        ]
    )
    env = os.environ.copy()
    env["PIP_DISABLE_PIP_VERSION_CHECK"] = "1"
    for command in commands:
        completed = subprocess.run(
            command,
            cwd=str(checkout),
            check=False,
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            env=env,
        )
        if completed.returncode != 0:
            detail = (completed.stderr or completed.stdout or "").strip()
            raise UpdateError(detail or f"reinstall failed: {' '.join(command)}")
