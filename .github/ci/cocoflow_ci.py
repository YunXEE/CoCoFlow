#!/usr/bin/env python3
"""Validate NUnit XML produced by a local Unity test run."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Dict, Optional, Sequence


COUNT_KEYS = ("total", "passed", "failed", "inconclusive", "skipped")


def unity_required(paths: Sequence[str], version_only: bool = False,
                   release_candidate: bool = False) -> bool:
    if release_candidate or not paths:
        return True
    return any(not (path.endswith(".md") or path.endswith(".md.meta") or
                    (version_only and path in ("package.json", "ValidationExceptions.json")))
               for path in paths)


def release_errors(root: Path, previous: str = "", tag: str = "") -> list[str]:
    version = json.loads((root / "package.json").read_text())["version"]
    errors = []
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", version):
        return ["Release package version must be X.Y.Z"]
    if previous and tuple(map(int, version.split("."))) <= tuple(map(int, previous.split("."))):
        errors.append("Package version must increase from the release base")
    headings = re.findall(r"^## \[([^\]]+)\]", (root / "CHANGELOG.md").read_text(), re.M)
    if not headings or headings[0] != version:
        errors.append("The newest Changelog entry must match package.json")
    for name in ("README.md", "README.zh-CN.md"):
        text = (root / name).read_text()
        refs = re.findall(r"https://github\.com/YunXEE/CoCoFlow\.git#v([0-9.]+)", text)
        if not refs or any(reference != version for reference in refs):
            errors.append(name + ": current installation references must match the package")
        if not re.search(r"\*\*(?:Version|版本)\*\*[:：]\s*" + re.escape(version) + r"(?![\d.])", text):
            errors.append(name + ": version banner does not match the package")
    if chr(96) + version + chr(96) not in (root / "Documentation~/index.md").read_text()[:500]:
        errors.append("The current documentation entry must identify the package version")
    exceptions = json.loads((root / "ValidationExceptions.json").read_text())
    for entries in exceptions.values():
        for entry in entries:
            if entry.get("PackageVersion") != version:
                errors.append("ValidationExceptions version does not match the package")
    if tag and tag != "v" + version:
        errors.append("Tag name must match package.json")
    return errors


def checked_suites(results: Path, prepared: Path) -> dict:
    expected = json.loads(prepared.read_text())["expectedTestAssemblies"]
    reports = {}
    for mode in ("editmode", "playmode"):
        path = results / (mode + "-results.xml")
        report = parse_unity_test_result(path)
        report["missingAssemblies"] = []
        if report.get("valid"):
            tree = ET.parse(path).getroot()
            actual = {node.get("name", "").removesuffix(".dll")
                      for node in tree.iter("test-suite") if node.get("type") == "Assembly"
                      and any(True for _ in node.iter("test-case"))}
            required = set(expected[mode])
            if mode == "editmode":
                required.add("CoCoFlow.CI.Tests.Editor")
                for name in ("BuildCallbacksNoOpWithoutStateGraphAssetsOrProvider",
                             "RequiredDependenciesAndOptionalModulesAreCompiled"):
                    cases = [case for case in tree.iter("test-case")
                             if case.get("name") == name]
                    if len(cases) != 1 or cases[0].get("result") != "Passed":
                        report.setdefault("requiredCaseErrors", []).append(name)
            report["missingAssemblies"] = sorted(required - actual)
        report["passedGate"] = (unity_test_passed(report) and
                                not report["missingAssemblies"] and
                                not report.get("requiredCaseErrors"))
        reports[mode] = report
    reports["dependencyEvidence"] = (results / "dependencies.json").is_file()
    reports["passedGate"] = (all(reports[mode]["passedGate"] for mode in expected)
                             and reports["dependencyEvidence"])
    return reports


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
    suite = subparsers.add_parser("unity-suite", help="check both modes and expected package test assemblies")
    suite.add_argument("results", type=Path)
    suite.add_argument("prepared", type=Path)
    release = subparsers.add_parser("release", help="check the current release identity")
    release.add_argument("--root", type=Path, default=Path.cwd())
    release.add_argument("--previous", default="")
    release.add_argument("--tag", default="")
    scope = subparsers.add_parser("scope", help="classify this candidate for the workflow")
    scope.add_argument("--base", default="")
    scope.add_argument("--release-candidate", action="store_true")
    scope.add_argument("--force-unity", action="store_true")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "scope":
        paths = []
        version_only = False
        if args.base and not re.fullmatch("0+", args.base):
            paths = subprocess.check_output(
                ["git", "diff", "--name-only", args.base, "HEAD"], text=True).splitlines()
            before = json.loads(subprocess.check_output(
                ["git", "show", args.base + ":package.json"], text=True))
            after = json.loads(Path("package.json").read_text())
            before.pop("version", None)
            after.pop("version", None)
            version_only = before == after
            if "ValidationExceptions.json" in paths:
                old_exceptions = json.loads(subprocess.check_output(
                    ["git", "show", args.base + ":ValidationExceptions.json"], text=True))
                new_exceptions = json.loads(Path("ValidationExceptions.json").read_text())
                for document in (old_exceptions, new_exceptions):
                    for entries in document.values():
                        for entry in entries:
                            entry.pop("PackageVersion", None)
                version_only = version_only and old_exceptions == new_exceptions
        required = args.force_unity or unity_required(paths, version_only, args.release_candidate)
        print("unity=" + str(required).lower())
        return 0
    if args.command == "release":
        errors = release_errors(args.root, args.previous, args.tag)
        print(json.dumps({"passedGate": not errors, "errors": errors}, ensure_ascii=False))
        return 1 if errors else 0
    if args.command == "unity-suite":
        result = checked_suites(args.results, args.prepared)
        print(json.dumps(result, ensure_ascii=False, sort_keys=True, indent=2))
        return 0 if result["passedGate"] else 1
    result = parse_unity_test_result(args.result)
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0 if unity_test_passed(result) else 1


if __name__ == "__main__":
    raise SystemExit(main())
