"""Player-facing command line for Vibe Snake."""

from __future__ import annotations

import argparse
import sys

from vibesnake import __version__
from vibesnake.checkout import DEFAULT_BRANCH, DEFAULT_REMOTE, find_checkout_root, radio_track_count
from vibesnake.update import UpdateError, update_checkout


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="vibesnake",
        description="Play Vibe Snake, check your install, or update from GitHub.",
    )
    parser.add_argument("--version", action="store_true", help="print the package version and exit")
    subparsers = parser.add_subparsers(dest="command")

    play = subparsers.add_parser("play", help="launch the game (default when no command is given)")
    play.set_defaults(command="play")

    update = subparsers.add_parser(
        "update",
        help=f"fast-forward this checkout from GitHub ({DEFAULT_BRANCH}) and reinstall",
    )
    update.add_argument(
        "--branch",
        default=DEFAULT_BRANCH,
        help=f"remote branch to pull (default: {DEFAULT_BRANCH})",
    )
    update.add_argument(
        "--remote",
        default="origin",
        help="git remote name (default: origin)",
    )
    update.add_argument(
        "--no-reinstall",
        action="store_true",
        help="pull code only; skip pip reinstall",
    )
    update.add_argument(
        "--dry-run",
        action="store_true",
        help="print the checkout identity without changing files",
    )

    subparsers.add_parser("version", help="print the installed package version")
    subparsers.add_parser("doctor", help="check Python, assets, radio library, and checkout health")
    return parser


def run_play() -> int:
    """Start the game."""
    # Import lazily so update/doctor stay light without pygame side effects.
    import traceback

    import pygame

    from vibesnake.core.game_state import Game

    try:
        Game().run()
    except Exception:
        print("[Main] Game crashed with error:")
        traceback.print_exc()
        return 1
    finally:
        pygame.quit()
    return 0


def run_version() -> int:
    print(f"vibe-snake {__version__}")
    return 0


def run_doctor() -> int:
    root = find_checkout_root()
    print(f"vibe-snake {__version__}")
    print(f"python {sys.version.split()[0]} ({sys.executable})")
    print(f"checkout {root if root else 'not found (package-only install?)'}")

    issues: list[str] = []
    if sys.version_info < (3, 11) or sys.version_info >= (3, 15):
        issues.append("Python 3.11 through 3.14 is required")

    try:
        import pygame  # noqa: F401
    except ImportError:
        issues.append("pygame is not installed; run the README install steps")

    if root is None:
        issues.append(f"source checkout with assets not found; clone {DEFAULT_REMOTE}")
    else:
        logo = root / "assets" / "images" / "logo.png"
        if not logo.is_file():
            issues.append(f"missing brand logo: {logo}")
        tracks = radio_track_count(root)
        print(f"radio tracks {tracks}")
        if tracks < 8:
            issues.append("radio library looks incomplete; expected the eight-station offline catalog")
        else:
            print("radio stations ready (GTA-style offline catalog)")

    if issues:
        print("doctor found issues:")
        for issue in issues:
            print(f"  - {issue}")
        return 1
    print("doctor: ready to play")
    print("launch with: vibesnake")
    print(f"update with: vibesnake update   # pulls {DEFAULT_REMOTE} @{DEFAULT_BRANCH}")
    return 0


def run_update(args: argparse.Namespace) -> int:
    try:
        result = update_checkout(
            branch=args.branch,
            remote=args.remote,
            reinstall=not args.no_reinstall,
            dry_run=args.dry_run,
        )
    except UpdateError as error:
        print(f"update failed: {error}", file=sys.stderr)
        return 1

    print(f"checkout {result['root']}")
    print(f"branch   {result['branch']}")
    print(f"before   {result['before']}")
    print(f"after    {result['after']}")
    print(f"changed  {result['changed']}")
    if result["mode"] == "dry-run":
        print("dry-run complete; no files changed")
    elif result["changed"] == "yes":
        print("update complete; run: vibesnake")
    else:
        print("already up to date with the remote branch")
    return 0


def main(argv: list[str] | None = None) -> int:
    """Dispatch player commands; bare `vibesnake` launches the game."""
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.version or args.command == "version":
        return run_version()
    command = args.command or "play"
    if command == "play":
        return run_play()
    if command == "doctor":
        return run_doctor()
    if command == "update":
        return run_update(args)
    parser.error(f"unknown command: {command}")
    return 2
