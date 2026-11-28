import json
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

import cocoflow_ci as ci
import prepare_unity_host as host
import release_receipt as receipts


ROOT = Path(__file__).resolve().parents[2]


class ScopeAndReleaseTests(unittest.TestCase):
    def test_pr_routes(self):
        for target in ("dev/0.4.8", "dev/0.5.0"):
            plan = ci.validation_plan("pull_request", target)
            self.assertTrue(plan["editor"])
            self.assertTrue(plan["api"])
            self.assertFalse(plan["release"])
        plan = ci.validation_plan("pull_request", "master", head_branch="dev/0.4.11")
        self.assertFalse(plan["editor"])
        self.assertTrue(plan["release"])
        self.assertTrue(plan["api"])
        self.assertEqual(["mono", "il2cpp"], plan["player_backends"])
        with self.assertRaises(ValueError):
            ci.validation_plan("pull_request", "master", head_branch="fix/example")
        plan = ci.validation_plan("pull_request", "experiment")
        self.assertFalse(plan["editor"] or plan["api"] or plan["release"])

    def test_push_verifies_only_the_receipt_and_manual_routes_are_diagnostic(self):
        plan = ci.validation_plan("push", ref_name="master")
        self.assertTrue(plan["master_push"])
        self.assertFalse(plan["editor"] or plan["release"] or plan["api"])
        self.assertTrue(ci.validation_plan("workflow_dispatch", ref_name="master")["editor"])
        for backend in ("mono", "il2cpp"):
            plan = ci.validation_plan("workflow_dispatch", ref_name="master",
                                      player_probe=True, player_backend=backend)
            self.assertFalse(plan["editor"] or plan["api"] or plan["release"])
            self.assertEqual([backend], plan["player_backends"])

    def test_gate_rejects_any_unsuccessful_required_job(self):
        dev = ("success", "true", "true", "false", "success", "success", "success", "skipped")
        release = ("success", "false", "true", "true", "skipped", "success", "success", "success")
        for results, required in ((dev, (0, 4, 5, 6)), (release, (0, 5, 6, 7))):
            self.assertEqual([], ci.gate_errors(*results))
            for index in required:
                for outcome in ("failure", "cancelled", "skipped", ""):
                    variation = list(results)
                    variation[index] = outcome
                    self.assertTrue(ci.gate_errors(*variation), (index, outcome))

    def test_release_receipt_binds_pr_candidate_tree_run_and_attempt(self):
        pull = {"number": 42, "head": {"sha": "head"}}
        run = {"id": 123, "run_attempt": 2}
        merged = {"tree": "tree", "base": "base"}
        receipt = {"pr": 42, "head": "head", "base": "base", "sha": "1" * 40,
                   "tree": "tree", "run": 123, "attempt": 2}
        self.assertTrue(receipts.matches(receipt, pull, run, merged))
        for field in receipt:
            self.assertFalse(receipts.matches({**receipt, field: "different"}, pull, run, merged), field)

    def test_release_identity_rejects_mismatched_refs_changelog_exceptions_and_tag(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            files = ("package.json", "CHANGELOG.md", "README.md", "README.zh-CN.md",
                     "Documentation~/index.md", "ValidationExceptions.json")
            for name in files:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / name, target)
            self.assertEqual([], ci.release_errors(root))
            version = json.loads((root / "package.json").read_text())["version"]
            self.assertTrue(ci.release_errors(root, previous=version))
            self.assertTrue(ci.release_errors(root, tag="v9.9.9"))
            for name in ("CHANGELOG.md", "README.md", "ValidationExceptions.json"):
                path = root / name
                original = path.read_text()
                path.write_text(original.replace(version, "9.9.9"))
                self.assertTrue(ci.release_errors(root), name)
                path.write_text(original)

    def test_changed_download_is_rejected_before_extraction(self):
        with self.assertRaises(ValueError):
            host.checked_bytes(b"unexpected archive", "0" * 64, "DOTween")


class FullSuiteTests(unittest.TestCase):
    def write_result(self, path, suites):
        total = sum(len(cases) for cases in suites.values())
        root = ET.Element("test-run", total=str(total), passed=str(total),
                          failed="0", inconclusive="0", skipped="0", result="Passed")
        for assembly, cases in suites.items():
            suite = ET.SubElement(root, "test-suite", type="Assembly", name=assembly + ".dll")
            for case in cases:
                ET.SubElement(suite, "test-case", name=case, result="Passed")
        ET.ElementTree(root).write(path)

    def test_green_xml_is_insufficient_when_optional_suites_or_required_cases_are_missing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            prepared = root / "prepared.json"
            prepared.write_text(json.dumps({"expectedTestAssemblies": {
                "editmode": ["CoCoFlow.Tests.Editor.StateGraph"],
                "playmode": ["CoCoFlow.Tests.Runtime.Localization"]}}))
            edit = {
                "CoCoFlow.Tests.Editor.StateGraph": [
                    "BuildCallbacksNoOpWithoutStateGraphAssetsOrProvider"],
                "CoCoFlow.CI.Tests.Editor": [
                    "RequiredDependenciesAndOptionalModulesAreCompiled"]}
            self.write_result(root / "editmode-results.xml", edit)
            self.write_result(root / "playmode-results.xml", {
                "CoCoFlow.Tests.Runtime.Localization": ["RealOptionalTest"]})
            (root / "dependencies.json").write_text("{}")
            self.assertTrue(ci.checked_suites(root, prepared)["passedGate"])
            self.write_result(root / "playmode-results.xml", {"UnrelatedTests": ["Other"]})
            self.assertFalse(ci.checked_suites(root, prepared)["passedGate"])
            self.write_result(root / "playmode-results.xml", {
                "CoCoFlow.Tests.Runtime.Localization": ["RealOptionalTest"]})
            tree = ET.parse(root / "editmode-results.xml")
            tree.getroot().attrib.update(passed="1", skipped="1")
            next(tree.iter("test-case")).set("result", "Skipped")
            tree.write(root / "editmode-results.xml")
            self.assertFalse(ci.checked_suites(root, prepared)["passedGate"])
            (root / "playmode-results.xml").unlink()
            self.assertFalse(ci.checked_suites(root, prepared)["passedGate"])


if __name__ == "__main__":
    unittest.main()
