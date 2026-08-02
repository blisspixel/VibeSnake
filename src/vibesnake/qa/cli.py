"""Command-line interface for automated gameplay QA campaigns."""

from __future__ import annotations

import argparse
from contextlib import nullcontext
import logging
from pathlib import Path

from vibesnake.qa.models import Scenario
from vibesnake.qa.policies import POLICY_NAMES
from vibesnake.qa.runner import report_json, run_campaign
from vibesnake.utils.logger import temporary_logger_level


def build_parser() -> argparse.ArgumentParser:
    """Create the QA command-line parser."""
    parser = argparse.ArgumentParser(
        prog="python -m vibesnake.qa",
        description="Run seeded Vibe Snake reference-core QA scenarios.",
    )
    parser.add_argument("--seeds", nargs="+", type=int, default=[0, 1, 2, 3, 4])
    parser.add_argument("--policies", nargs="+", choices=POLICY_NAMES, default=list(POLICY_NAMES))
    parser.add_argument("--steps", type=_positive_int, default=500)
    parser.add_argument("--step-seconds", type=_positive_float, default=0.05)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--compact", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument(
        "--skip-determinism-check",
        action="store_true",
        help="Run each scenario once instead of comparing an immediate replay.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    """Run a campaign and return a CI-friendly process status."""
    args = build_parser().parse_args(argv)
    logger_scope = temporary_logger_level("vibesnake", logging.WARNING) if not args.verbose else nullcontext()
    with logger_scope:
        scenarios = [
            Scenario(
                policy=policy,
                seed=seed,
                max_steps=args.steps,
                step_seconds=args.step_seconds,
            )
            for policy in args.policies
            for seed in args.seeds
        ]
        report = run_campaign(
            scenarios,
            verify_determinism=not args.skip_determinism_check,
        )
        payload = report_json(report, pretty=not args.compact)

        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(payload, encoding="utf-8")
            print(
                f"QA campaign {'passed' if report.passed else 'failed'}: "
                f"{report.aggregates['passed']}/{report.aggregates['scenarios']} scenarios; "
                f"report={args.output}"
            )
        else:
            print(payload, end="")

    return 0 if report.passed else 1


def _positive_int(value: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be greater than zero")
    return parsed


def _positive_float(value: str) -> float:
    parsed = float(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be greater than zero")
    return parsed
