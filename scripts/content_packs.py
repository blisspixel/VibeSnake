"""Validate canonical core and optional content-pack manifests."""

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
)
from vibesnake.content.packs import (  # noqa: E402
    CURRENT_RULESET_ID,
    CURRENT_RULESET_VERSION,
    ContentPackError,
    check_pack_manifest,
    resolve_pack_set,
)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse an explicit manifest qualification request."""
    parser = argparse.ArgumentParser(description="Validate Vibe Snake core and optional content-pack manifests.")
    parser.add_argument(
        "manifests",
        nargs="+",
        type=Path,
        help="Canonical manifest paths. Exactly one must declare kind core.",
    )
    parser.add_argument(
        "--inventory",
        type=Path,
        default=ROOT / "config" / "content_inventory.json",
    )
    parser.add_argument("--game-version", default="0.3.0")
    parser.add_argument("--ruleset-id", default=CURRENT_RULESET_ID)
    parser.add_argument("--ruleset-version", type=int, default=CURRENT_RULESET_VERSION)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Validate files, resolve compatibility, and print an actionable result."""
    args = parse_args(argv)
    try:
        inventory = check_inventory(ROOT, inventory_path=args.inventory)
        manifests = [check_pack_manifest(path.resolve(), inventory) for path in args.manifests]
        core = [manifest for manifest in manifests if manifest["kind"] == "core"]
        if len(core) != 1:
            raise ContentPackError(f"expected exactly one core manifest, found {len(core)}")
        optional = [manifest for manifest in manifests if manifest["kind"] != "core"]
        resolution = resolve_pack_set(
            core[0],
            optional,
            inventory,
            game_version=args.game_version,
            ruleset_id=args.ruleset_id,
            ruleset_version=args.ruleset_version,
        )
        if not resolution.core_ready:
            print(f"Content pack core rejected: {resolution.core.message}")
            return 1
        if resolution.rejected_optional:
            print("Content pack qualification rejected optional content:")
            for pack_id, result in resolution.rejected_optional.items():
                print(f"  {pack_id}: {result.code}: {result.message}")
            return 1
        print(f"Content packs qualified: core={core[0]['id']} optional={len(resolution.accepted_optional)}")
        return 0
    except (ContentInventoryError, ContentPackError) as error:
        print(f"Content pack qualification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
