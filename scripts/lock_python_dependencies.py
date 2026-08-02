"""Command-line wrapper for the hash-locked Python CI environment."""

from pathlib import Path

from _checkout import promote_checkout_source

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = promote_checkout_source(REPOSITORY_ROOT)

from vibesnake.qa.dependency_lock import main  # noqa: E402


if __name__ == "__main__":
    raise SystemExit(main(repository_root=REPOSITORY_ROOT))
