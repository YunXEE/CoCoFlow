#!/usr/bin/env python3
"""Record Player cases and assemblies; require declared scope, build and seat evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

import cocoflow_ci as ci


def collect_cases(root):
    records = []

    def walk(node, assembly=None):
        if node.tag == "test-suite" and node.get("type") == "Assembly":
            assembly = node.get("name", "").replace("\\", "/").rsplit("/", 1)[-1].removesuffix(".dll")
        if node.tag == "test-case":
            records.append({"assembly": assembly, "id": node.get("id"),
                "name": node.get("fullname"), "result": node.get("result"),
                "label": node.get("label"),
                "message": node.findtext("failure/message") or node.findtext("reason/message"),
                "stack": node.findtext("failure/stack-trace")})
        for child in node:
            walk(child, assembly)

    walk(root)
    return records


def check(results, prepared):
    metadata = json.loads(prepared.read_text())
    required = metadata["playerProbe"]
    report = ci.parse_unity_test_result(results / "standalone-results.xml")
    scope = required.get("scope", "probe")
    case_records, assembly_results = [], {}
    missing_assemblies = sorted(metadata.get("expectedPlayerAssemblies", [])) if scope == "all" else []
    errors = []
    if not ci.unity_test_passed(report) or report.get("skipped") != 0:
        errors.append("Player XML must be nonempty, Passed and have no failed/inconclusive/skipped cases")
    if report.get("valid"):
        root = ET.parse(results / "standalone-results.xml").getroot()
        cases = list(root.iter("test-case"))
        case_records = collect_cases(root)
        for record in case_records:
            name = record["assembly"]
            if name:
                counts = assembly_results.setdefault(name, {"total": 0, "Passed": 0,
                    "Failed": 0, "Skipped": 0, "Inconclusive": 0})
                counts["total"] += 1
                status = record["result"]
                counts[status] = counts.get(status, 0) + 1
        if scope == "all":
            missing_assemblies = sorted(set(metadata["expectedPlayerAssemblies"]) - set(assembly_results))
            if missing_assemblies:
                errors.append("Player test assemblies not observed: " + ", ".join(missing_assemblies))
        for name in required["expectedCases"]:
            matches = [case for case in cases if case.get("fullname") == name]
            if len(matches) != 1 or matches[0].get("result") != "Passed":
                errors.append("Required Player case missing, duplicated or not Passed: " + name)
        if report.get("total") != len(cases):
            errors.append("Player XML case count does not match its summary")
        if scope != "all" and len(cases) != len(required["expectedCases"]):
            errors.append("The Player probe must execute exactly the declared cases")
    settings_path = results / "player-build-settings.json"
    settings = json.loads(settings_path.read_text()) if settings_path.is_file() else {}
    for key in ("target", "backend", "stripping"):
        if settings.get(key) != required[key]:
            errors.append("Player build setting mismatch: " + key)
    if settings.get("unity") != metadata["unity"]:
        errors.append("Player build Unity version mismatch")
    preservation = metadata.get("diagnosticPreservation")
    if (settings.get("diagnosticPreservationSha256") or None) != (preservation or {}).get("sha256"):
        errors.append("Diagnostic preservation file does not match the declared experiment")
    executable = Path(settings.get("outputPath", ""))
    executable_hash = None
    if not executable.is_file() or executable.stat().st_size == 0:
        errors.append("Built Player executable missing or empty")
    else:
        executable_hash = hashlib.sha256(executable.read_bytes()).hexdigest()
    receipt_path = results / "license-return.json"
    receipt = json.loads(receipt_path.read_text()) if receipt_path.is_file() else {}
    if receipt.get("activationObserved") is not True or receipt.get("confirmed") is not True:
        errors.append("This run's activation and Personal seat return must be confirmed")
    return {"passedProbe": not errors, "sourceSha": metadata["sourceSha"],
            "scope": scope, "assemblyResults": assembly_results,
            "expectedPlayerAssemblies": metadata.get("expectedPlayerAssemblies", []),
            "missingPlayerAssemblies": missing_assemblies, "caseResults": case_records,
            "diagnosticPreservation": preservation,
            "playerResults": report, "buildSettings": settings,
            "executableSha256": executable_hash, "errors": errors}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", type=Path)
    parser.add_argument("prepared", type=Path)
    args = parser.parse_args()
    result = check(args.results, args.prepared)
    (args.results / "player-probe-summary.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({key: value for key, value in result.items() if key != "caseResults"}, indent=2))
    raise SystemExit(0 if result["passedProbe"] else 1)
