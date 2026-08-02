#!/usr/bin/env python3
"""Validate NUnit XML produced by a local Unity test run."""

from __future__ import annotations

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Dict, Optional, Sequence


COUNT_KEYS = ("total", "passed", "failed", "inconclusive", "skipped")


def parse_unity_test_result(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        return {
            "available": False,
            "valid": False,
            "parseError": "result XML is missing",
        }

    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        return {
            "available": True,
            "valid": False,
            "parseError": str(error),
        }

    if root.tag != "test-run":
        return {
            "available": True,
            "valid": False,
            "parseError": "result XML root must be test-run",
        }

    result: Dict[str, Any] = {"available": True}
    try:
        for key in COUNT_KEYS:
            result[key] = int(root.attrib[key])
    except (KeyError, ValueError) as error:
        result.update({"valid": False, "parseError": str(error)})
        return result

    counts_are_valid = (
        result["total"] > 0
        and all(result[key] >= 0 for key in COUNT_KEYS)
        and sum(result[key] for key in COUNT_KEYS[1:]) == result["total"]
    )
    if not counts_are_valid:
        result.update(
            {
                "valid": False,
                "parseError": "result XML contains invalid test counts",
            }
        )
        return result

    result["result"] = root.attrib.get("result")
    result["valid"] = True
    return result


def unity_test_passed(result: Dict[str, Any]) -> bool:
    return (
        result.get("valid") is True
        and result.get("result") == "Passed"
        and result.get("failed") == 0
        and result.get("inconclusive") == 0
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    unity_result = subparsers.add_parser(
        "unity-result",
        help="validate one NUnit XML file produced by a local Unity test run",
    )
    unity_result.add_argument("result", type=Path)
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    result = parse_unity_test_result(args.result)
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0 if unity_test_passed(result) else 1


if __name__ == "__main__":
    raise SystemExit(main())
