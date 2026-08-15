"""Contracts for the documented player launchers.

A playtester on Linux followed the published `./play.sh` command from a fresh
source archive and never reached a window: the archive carried the script without
an execute bit, and the PowerShell launcher then failed to bind its own
arguments. Both failures happen before any game code runs, so they are checked
here rather than in the native suite.
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

import pytest

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]

# Shell entry points are exec'd directly by the documented command, so the tracked
# mode has to carry the execute bit into every clone and published archive.
EXECUTABLE_SCRIPTS = ("play.sh", "scripts/install_player.sh")


def _tracked_modes() -> dict[str, str]:
    completed = subprocess.run(
        ["git", "ls-files", "-s"],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    modes: dict[str, str] = {}
    for line in completed.stdout.splitlines():
        if not line.strip():
            continue
        metadata, path = line.split("\t", 1)
        modes[path] = metadata.split(" ", 1)[0]
    return modes


@pytest.mark.parametrize("relative_path", EXECUTABLE_SCRIPTS)
def test_documented_shell_launchers_are_tracked_executable(relative_path: str) -> None:
    modes = _tracked_modes()
    assert relative_path in modes, f"{relative_path} is not tracked"
    assert modes[relative_path] == "100755", (
        f"{relative_path} must be tracked executable so `./{relative_path}` works "
        "from a clone and from the published source archive"
    )


def test_every_shebang_script_is_tracked_executable() -> None:
    modes = _tracked_modes()
    missing = []
    for path, mode in sorted(modes.items()):
        if not path.endswith(".sh"):
            continue
        absolute = REPOSITORY_ROOT / path
        if not absolute.is_file():
            continue
        first_line = absolute.read_text(encoding="utf-8").splitlines()[:1]
        if first_line and first_line[0].startswith("#!") and mode != "100755":
            missing.append(f"{path} ({mode})")
    assert not missing, f"shebang scripts must be tracked executable: {missing}"


def test_powershell_launcher_forwards_arguments_without_parameter_binding() -> None:
    launcher = (REPOSITORY_ROOT / "play.ps1").read_text(encoding="utf-8")
    # The launcher documents why it avoids CmdletBinding, so only code is inspected.
    code = "\n".join(line for line in launcher.splitlines() if not line.lstrip().startswith("#"))

    # An advanced script leaves $args undefined under StrictMode and binds
    # --agent-watch-pipe=<name> as a PowerShell parameter instead of forwarding it.
    assert "[CmdletBinding()]" not in code, (
        "play.ps1 must stay a simple script so Godot user arguments are forwarded "
        "instead of bound as PowerShell parameters"
    )
    assert "Set-StrictMode -Version Latest" in code
    # Arguments are captured before any other work so a launch failure can never be
    # an argument-binding error, and the no-argument case stays defined.
    assert re.search(r"\$forwardedArguments\s*=\s*@\(\)", launcher)
    assert "if ($null -ne $args)" in code
    assert "@forwardedArguments" in code
    assert "--path $gamePath" in code
    assert "assert_godot_import.ps1" in code


def test_shell_launcher_forwards_its_own_arguments() -> None:
    launcher = (REPOSITORY_ROOT / "play.sh").read_text(encoding="utf-8")

    assert launcher.startswith("#!"), "play.sh must keep its shebang"
    assert '"$@"' in launcher, "play.sh must forward its arguments to play.ps1"
