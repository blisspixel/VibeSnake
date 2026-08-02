"""Check executable anti-slop rules across active source and canonical docs."""

from __future__ import annotations

from pathlib import Path

from _checkout import promote_checkout_source

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = promote_checkout_source(REPOSITORY_ROOT)

from vibesnake.qa.source_policy import inspect_repository, policy_files  # noqa: E402


def main() -> int:
    """Print deterministic policy diagnostics and return a process status."""
    violations = inspect_repository(REPOSITORY_ROOT)
    if violations:
        print("Source policy check failed:")
        for violation in violations:
            print(f"  {violation.render()}")
        return 1

    print(f"Source policy check passed for {len(policy_files(REPOSITORY_ROOT))} active text files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
