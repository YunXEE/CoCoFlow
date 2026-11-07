import json
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

import cocoflow_ci as ci
import prepare_unity_host as host


ROOT = Path(__file__).resolve().parents[2]


class ScopeAndReleaseTests(unittest.TestCase):
    def test_docs_can_skip_but_unknown_inputs_and_release_candidates_cannot(self):
        self.assertFalse(ci.unity_required(["README.md", "Documentation~/index.md"]))
        self.assertFalse(ci.unity_required(["package.json"], version_only=True))
        for paths in ([], ["Tests/new-test.cs"], [".github/workflows/ci-static.yml"],
                      ["package.json"], ["Unknown.asset"]):
            self.assertTrue(ci.unity_required(paths))
        self.assertTrue(ci.unity_required(["README.md"], release_candidate=True))

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
