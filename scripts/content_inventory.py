"""Generate or verify the deterministic source-asset inventory."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

from _checkout import promote_checkout_source

ROOT = Path(__file__).resolve().parents[1]
SRC = promote_checkout_source(ROOT)

from vibesnake.content.inventory import (  # noqa: E402
    ContentInventoryError,
    check_inventory,
    release_blockers,
    write_inventory,
)


def parse_args() -> argparse.Namespace:
    """Parse the explicit inventory operation."""
    parser = argparse.ArgumentParser(description="Generate or verify Vibe Snake's deterministic asset inventory.")
    operation = parser.add_mutually_exclusive_group()
    operation.add_argument(
        "--write",
        action="store_true",
        help="Regenerate config/content_inventory.json from current asset bytes.",
    )
    operation.add_argument(
        "--check",
        action="store_true",
        help="Verify the checked-in inventory exactly matches policy and assets.",
    )
    parser.add_argument(
        "--release-ready",
        action="store_true",
        help="Also fail while any runtime asset is blocked or lacks cleared rights.",
    )
    return parser.parse_args()


def main() -> int:
    """Run inventory generation or verification and print a concise summary."""
    args = parse_args()
    try:
        if args.write:
            inventory = write_inventory(ROOT)
            action = "written"
        else:
            inventory = check_inventory(ROOT)
            action = "verified"

        blockers = release_blockers(inventory)
        if args.release_ready and blockers:
            print("Content inventory is not release-ready:")
            for blocker in blockers:
                print(f"  {blocker}")
            return 1

        summary = inventory["summary"]
        print(
            "Content inventory "
            f"{action}: files={inventory['fileCount']} bytes={inventory['totalBytes']} "
            f"eligible={summary['exportEligibleFileCount']} "
            f"duplicates={summary['duplicateFileCount']} "
            f"release_blockers={len(blockers)}"
        )
        return 0
    except ContentInventoryError as error:
        print(f"Content inventory failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
