import json
import tempfile
import unittest
from pathlib import Path
import xml.etree.ElementTree as ET
from unittest.mock import patch

import check_player_probe as probe
import prepare_player_probe as prepare


class PlayerProbeTests(unittest.TestCase):
    def full_fixture(self, root):
        prepared, xml, settings = self.fixture(root)
        metadata = json.loads(prepared.read_text())
        metadata["playerProbe"]["scope"] = "all"
        metadata["playerProbe"]["expectedCases"] = metadata["playerProbe"]["expectedCases"][:1]
        metadata["expectedPlayerAssemblies"] = ["CoCoFlow.CI.Tests.Player", "CoCoFlow.Tests.Runtime.Example"]
        prepared.write_text(json.dumps(metadata))
        cases = list(xml)
        for case in cases:
            xml.remove(case)
        for name, children in zip(metadata["expectedPlayerAssemblies"], (cases[:1], cases[1:])):
            suite = ET.SubElement(xml, "test-suite", type="Assembly", name=name + ".dll")
            suite.extend(children)
        ET.ElementTree(xml).write(root / "standalone-results.xml")
        return prepared, xml, cases, settings

    def fixture(self, root, backend="mono"):
        config = "player-probe-il2cpp.json" if backend == "il2cpp" else "player-probe.json"
        required = json.loads(Path(probe.__file__).with_name(config).read_text())
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

    def test_full_scope_accepts_all_discovered_cases_and_requires_every_assembly(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prepared, _, cases, _ = self.full_fixture(root)
            report = probe.check(root, prepared)
            self.assertTrue(report["passedProbe"])
            self.assertEqual(len(cases), len(report["caseResults"]))
            self.assertEqual(6, report["assemblyResults"]["CoCoFlow.Tests.Runtime.Example"]["Passed"])
            metadata = json.loads(prepared.read_text())
            metadata["expectedPlayerAssemblies"].append("Missing.Runtime.Assembly")
            prepared.write_text(json.dumps(metadata))
            report = probe.check(root, prepared)
            self.assertFalse(report["passedProbe"])
            self.assertEqual(["Missing.Runtime.Assembly"], report["missingPlayerAssemblies"])
            (root / "standalone-results.xml").unlink()
            report = probe.check(root, prepared)
            self.assertFalse(report["passedProbe"])
            self.assertEqual(sorted(metadata["expectedPlayerAssemblies"]), report["missingPlayerAssemblies"])
            self.assertEqual([], report["caseResults"])

    def test_full_scope_preserves_failures_and_skips_with_their_reasons(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prepared, xml, cases, _ = self.full_fixture(root)
            xml.set("passed", "5")
            xml.set("failed", "1")
            xml.set("skipped", "1")
            xml.set("result", "Failed")
            cases[1].set("result", "Failed")
            failure = ET.SubElement(cases[1], "failure")
            ET.SubElement(failure, "message").text = "real failure"
            ET.SubElement(failure, "stack-trace").text = "actual call chain"
            cases[2].set("result", "Skipped")
            ET.SubElement(ET.SubElement(cases[2], "reason"), "message").text = "platform excluded"
            ET.ElementTree(xml).write(root / "standalone-results.xml")
            report = probe.check(root, prepared)
            self.assertFalse(report["passedProbe"])
            failed = next(case for case in report["caseResults"] if case["result"] == "Failed")
            skipped = next(case for case in report["caseResults"] if case["result"] == "Skipped")
            self.assertEqual("real failure", failed["message"])
            self.assertEqual("actual call chain", failed["stack"])
            self.assertEqual("platform excluded", skipped["message"])

    def test_full_scope_does_not_allow_diagnostic_preservation(self):
        with patch.object(prepare.host, "prepare") as make_host:
            with self.assertRaisesRegex(ValueError, "unmodified preservation"):
                prepare.prepare(Path("unused"), Path("unused"), "il2cpp", True, "all")
            make_host.assert_not_called()

    def test_il2cpp_requires_its_own_backend_guard_and_stripping_receipt(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prepared, xml, settings = self.fixture(root, "il2cpp")
            self.assertTrue(probe.check(root, prepared)["passedProbe"])
            settings["backend"] = "Mono2x"
            (root / "player-build-settings.json").write_text(json.dumps(settings))
            self.assertFalse(probe.check(root, prepared)["passedProbe"])
            settings["backend"] = "IL2CPP"
            (root / "player-build-settings.json").write_text(json.dumps(settings))
            list(xml)[0].set("fullname", "CoCoFlow.CI.PlayerProbeTests.UnexpectedEnvironmentCheck")
            ET.ElementTree(xml).write(root / "standalone-results.xml")
            self.assertFalse(probe.check(root, prepared)["passedProbe"])

    def test_preservation_diagnostic_requires_a_matching_file_receipt_and_stays_labelled(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prepared, _, settings = self.fixture(root, "il2cpp")
            metadata = json.loads(prepared.read_text())
            metadata["diagnosticPreservation"] = {"sha256": "declared-file"}
            prepared.write_text(json.dumps(metadata))
            self.assertFalse(probe.check(root, prepared)["passedProbe"])
            settings["diagnosticPreservationSha256"] = "declared-file"
            (root / "player-build-settings.json").write_text(json.dumps(settings))
            report = probe.check(root, prepared)
            self.assertTrue(report["passedProbe"])
            self.assertEqual(metadata["diagnosticPreservation"], report["diagnosticPreservation"])
            del metadata["diagnosticPreservation"]
            prepared.write_text(json.dumps(metadata))
            self.assertFalse(probe.check(root, prepared)["passedProbe"])

    def test_host_preparation_selects_the_requested_profile_and_callback_requirements(self):
        for backend, preserve, scope, backend_value, stripping_value in (
                ("mono", False, "probe", 0, 0), ("il2cpp", False, "probe", 1, 2),
                ("il2cpp", True, "probe", 1, 2), ("mono", False, "all", 0, 0), ("il2cpp", False, "all", 1, 2)):
            with self.subTest(backend=backend, preserve=preserve, scope=scope), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                (root / "host/ProjectSettings").mkdir(parents=True)
                (root / "host/Assets/Editor").mkdir(parents=True)
                settings = root / "host/ProjectSettings/ProjectSettings.asset"
                settings.write_text("PlayerSettings:\n")
                with patch.object(prepare.host, "prepare", return_value={"sourceSha": "candidate",
                        "expectedTestAssemblies": {"playmode": ["Package.Runtime.Tests"]}}):
                    metadata = prepare.prepare(root, Path("unused"), backend, preserve, scope)
                expected = metadata["playerProbe"]
                required = json.loads((root / "host/CiPlayerProbeRequirements.json").read_text())
                self.assertEqual(expected, required)
                self.assertEqual(expected, json.loads((root / "prepared.json").read_text())["playerProbe"])
                self.assertIn(f"scriptingBackend:\n    Standalone: {backend_value}\n", settings.read_text())
                self.assertIn(f"managedStrippingLevel:\n    Standalone: {stripping_value}\n", settings.read_text())
                self.assertTrue((root / "host/Assets/Editor/CoCoFlowCiPlayerBuild.cs").is_file())
                link = root / "host/Assets/PlayerProbe/link.xml"
                self.assertEqual(preserve, link.is_file())
                guard = (root / "host/Assets/PlayerProbe/CoCoFlowCiPlayerProbe.cs").read_text()
                self.assertNotIn("__COCOFLOW_EXPECTED_BACKEND__", guard)
                self.assertIn('"' + expected["backend"] + '"', guard)
                if scope == "all":
                    self.assertEqual(1, len(expected["expectedCases"]))
                    self.assertEqual(["Package.Runtime.Tests", "CoCoFlow.CI.Tests.Player"], metadata["expectedPlayerAssemblies"])

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
