import json
import subprocess
import tempfile
import unittest
from pathlib import Path

import cocoflow_ci as ci


class TemporaryRepositoryTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, relative, content=""):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def write_json(self, relative, value):
        return self.write(relative, json.dumps(value))

    def meta(self, relative, guid):
        return self.write(relative + ".meta", "fileFormatVersion: 2\nguid: {0}\n".format(guid))

    def test_strict_json_rejects_duplicate_keys(self):
        path = self.write("duplicate.json", '{"value": 1, "value": 2}')
        with self.assertRaises(ci.DuplicateJsonKey):
            ci.load_json_strict(path)

    def test_semver_rejects_numeric_prerelease_leading_zero(self):
        self.assertTrue(ci.is_strict_semver("0.4.0-pre.15"))
        self.assertFalse(ci.is_strict_semver("0.4.0-pre.015"))

    def test_meta_checker_detects_missing_meta_and_duplicate_guid(self):
        self.write("Runtime/One.cs")
        self.write("Runtime/Two.cs")
        self.meta("Runtime/Two.cs", "1" * 32)
        self.write("Runtime.meta", "fileFormatVersion: 2\nguid: {0}\n".format("2" * 32))
        self.write("Other.meta", "fileFormatVersion: 2\nguid: {0}\n".format("1" * 32))
        tracked = [
            "Runtime/One.cs",
            "Runtime/Two.cs",
            "Runtime/Two.cs.meta",
            "Runtime.meta",
            "Other.meta",
        ]
        validator = ci.RepositoryValidator(self.root, tracked)
        validator.check_meta_and_guids()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("missing-meta", codes)
        self.assertIn("duplicate-guid", codes)

    def test_path_checker_handles_case_and_compound_forbidden_ending(self):
        self.write("Runtime/Thing.cs")
        self.write("runtime/thing.cs")
        self.write("Runtime/cache.vc.db")
        tracked = ["Runtime/Thing.cs", "runtime/thing.cs", "Runtime/cache.vc.db"]
        validator = ci.RepositoryValidator(self.root, tracked)
        validator.check_paths_and_forbidden_files()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("case-collision", codes)
        self.assertIn("forbidden-ending", codes)

    def test_assembly_names_collide_case_insensitively(self):
        self.write_json("Runtime/One.asmdef", {"name": "CoCoFlow.Runtime.One"})
        self.write_json("Runtime/Two.asmdef", {"name": "cocoflow.runtime.one"})
        validator = ci.RepositoryValidator(
            self.root, ["Runtime/One.asmdef", "Runtime/Two.asmdef"]
        )
        validator.check_assemblies()
        self.assertIn(
            "asmdef-duplicate-name",
            {finding.code for finding in validator.findings},
        )

    def test_runtime_cannot_reference_include_platform_editor_assembly(self):
        self.write_json(
            "Runtime/Main.asmdef",
            {"name": "CoCoFlow.Runtime.Main", "references": ["CoCoFlow.Tools"]},
        )
        self.write_json(
            "Tools/Tools.asmdef",
            {
                "name": "CoCoFlow.Tools",
                "references": [],
                "includePlatforms": ["Editor"],
            },
        )
        validator = ci.RepositoryValidator(
            self.root, ["Runtime/Main.asmdef", "Tools/Tools.asmdef"]
        )
        validator.check_assemblies()
        self.assertIn(
            "runtime-editor-reference",
            {finding.code for finding in validator.findings},
        )

    def test_external_guid_reference_is_not_a_false_error(self):
        self.write_json(
            "Runtime/Main.asmdef",
            {"name": "CoCoFlow.Runtime.Main", "references": ["GUID:" + "a" * 32]},
        )
        validator = ci.RepositoryValidator(self.root, ["Runtime/Main.asmdef"])
        validator.check_assemblies()
        self.assertNotIn(
            "asmdef-guid-reference",
            {finding.code for finding in validator.findings},
        )


class GitSnapshotTests(unittest.TestCase):
    def test_materialize_head_excludes_dirty_and_untracked_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            output = Path(temporary) / "snapshot"
            root.mkdir()
            subprocess.run(["git", "init", "-q"], cwd=str(root), check=True)
            subprocess.run(
                ["git", "config", "user.email", "ci@example.invalid"],
                cwd=str(root),
                check=True,
            )
            subprocess.run(
                ["git", "config", "user.name", "CI"],
                cwd=str(root),
                check=True,
            )
            tracked = root / "tracked.txt"
            tracked.write_text("committed", encoding="utf-8")
            subprocess.run(["git", "add", "tracked.txt"], cwd=str(root), check=True)
            subprocess.run(["git", "commit", "-qm", "fixture"], cwd=str(root), check=True)
            tracked.write_text("dirty", encoding="utf-8")
            (root / "untracked.txt").write_text("untracked", encoding="utf-8")

            sha = ci.materialize_head(root, output)

            self.assertEqual(
                subprocess.check_output(
                    ["git", "rev-parse", "HEAD"], cwd=str(root), text=True
                ).strip(),
                sha,
            )
            self.assertEqual("committed", (output / "tracked.txt").read_text(encoding="utf-8"))
            self.assertFalse((output / "untracked.txt").exists())


class UnityHarnessTests(unittest.TestCase):
    def test_manifest_uses_snapshot_file_uri_and_testables(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary) / "package"
            host = Path(temporary) / "host"
            package.mkdir()
            ci.create_clean_host(host, package, "6000.3.20f1")
            manifest = json.loads(
                (host / "Packages/manifest.json").read_text(encoding="utf-8")
            )
            self.assertEqual([ci.PACKAGE_NAME], manifest["testables"])
            self.assertTrue(manifest["dependencies"][ci.PACKAGE_NAME].startswith("file:"))
            self.assertEqual(
                "1.0.0",
                manifest["dependencies"]["com.unity.modules.animation"],
            )

    def test_test_command_does_not_quit_before_results_are_written(self):
        command = ci.unity_command(
            Path("/Unity"),
            Path("/host"),
            Path("/log"),
            "editmode",
            Path("/result.xml"),
        )
        self.assertIn("-runTests", command)
        self.assertNotIn("-quit", command)

    def test_missing_or_malformed_result_is_invalid(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            self.assertFalse(ci.parse_unity_test_result(result)["valid"])
            result.write_text("<test-run", encoding="utf-8")
            self.assertFalse(ci.parse_unity_test_result(result)["valid"])

    def test_valid_nonempty_result_is_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            result.write_text(
                '<test-run total="2" passed="2" failed="0" inconclusive="0" skipped="0" result="Passed" />',
                encoding="utf-8",
            )
            parsed = ci.parse_unity_test_result(result)
            self.assertTrue(parsed["valid"])
            self.assertEqual(2, parsed["total"])


if __name__ == "__main__":
    unittest.main()
