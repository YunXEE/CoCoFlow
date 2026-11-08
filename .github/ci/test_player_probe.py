import json
import tempfile
import unittest
from pathlib import Path
import xml.etree.ElementTree as ET

import check_player_probe as probe


class PlayerProbeTests(unittest.TestCase):
    def fixture(self, root):
        required = json.loads(Path(probe.__file__).with_name("player-probe.json").read_text())
        prepared = root / "prepared.json"
        prepared.write_text(json.dumps({"sourceSha": "candidate", "unity": "6000.3.27f2", "playerProbe": required}))
        cases = required["expectedCases"]
        xml = ET.Element("test-run", total=str(len(cases)), passed=str(len(cases)), failed="0",
                         skipped="0", inconclusive="0", result="Passed")
        for name in cases:
            ET.SubElement(xml, "test-case", fullname=name, result="Passed")
        ET.ElementTree(xml).write(root / "standalone-results.xml")
        exe = root / "Probe.exe"
        exe.write_bytes(b"nonempty-build-fixture")
        settings = {key: required[key] for key in ("target", "backend", "stripping")}
        settings.update(unity="6000.3.27f2", outputPath=str(exe))
        (root / "player-build-settings.json").write_text(json.dumps(settings))
        (root / "license-return.json").write_text(json.dumps({"activationObserved": True, "confirmed": True}))
        return prepared, xml, settings

    def test_complete_player_evidence_passes_and_binds_executable(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prepared, _, _ = self.fixture(root)
            result = probe.check(root, prepared)
            self.assertTrue(result["passedProbe"])
            self.assertEqual("candidate", result["sourceSha"])
            self.assertEqual(64, len(result["executableSha256"]))

    def test_green_totals_do_not_hide_missing_or_duplicated_required_cases(self):
        for kind in ("missing", "duplicate", "summary-mismatch"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                prepared, xml, _ = self.fixture(root)
                if kind == "missing":
                    xml.remove(list(xml)[0])
                elif kind == "duplicate":
                    list(xml)[0].set("fullname", list(xml)[1].get("fullname"))
                else:
                    xml.set("total", "1")
                    xml.set("passed", "1")
                ET.ElementTree(xml).write(root / "standalone-results.xml")
                self.assertFalse(probe.check(root, prepared)["passedProbe"])

    def test_wrong_build_configuration_and_missing_executable_do_not_pass(self):
        for key, value in (("target", "StandaloneLinux64"), ("backend", "IL2CPP"),
                           ("stripping", "Medium"), ("unity", "other"), ("outputPath", "missing.exe")):
            with self.subTest(key=key), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                prepared, _, settings = self.fixture(root)
                settings[key] = value
                (root / "player-build-settings.json").write_text(json.dumps(settings))
                self.assertFalse(probe.check(root, prepared)["passedProbe"])

    def test_unconfirmed_return_missing_xml_and_skipped_cases_do_not_pass(self):
        for kind in ("return", "missing-xml", "skipped"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                prepared, xml, _ = self.fixture(root)
                if kind == "return":
                    (root / "license-return.json").write_text('{"activationObserved":true,"confirmed":false}')
                elif kind == "missing-xml":
                    (root / "standalone-results.xml").unlink()
                else:
                    xml.set("passed", str(len(xml) - 1))
                    xml.set("skipped", "1")
                    list(xml)[0].set("result", "Skipped")
                    ET.ElementTree(xml).write(root / "standalone-results.xml")
                self.assertFalse(probe.check(root, prepared)["passedProbe"])


if __name__ == "__main__":
    unittest.main()
