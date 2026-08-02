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

    def test_strict_json_rejects_non_finite_constants(self):
        for constant in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(constant=constant):
                path = self.write("constant.json", '{"value": ' + constant + "}")
                with self.assertRaises(ci.InvalidJsonConstant):
                    ci.load_json_strict(path)

    def test_semver_rejects_numeric_prerelease_leading_zero(self):
        self.assertTrue(ci.is_strict_semver("0.4.0-pre.15"))
        self.assertFalse(ci.is_strict_semver("0.4.0-pre.015"))

    def test_meta_checker_detects_missing_meta_and_duplicate_guid(self):
        self.write("Runtime/One.cs")
        self.write("Runtime/Two.cs")
        self.meta("Runtime/Two.cs", "1" * 32)
        self.write("Runtime.meta", "fileFormatVersion: 2\nguid: {0}\n".format("2" * 32))
        self.write("Other")
        self.write("Other.meta", "fileFormatVersion: 2\nguid: {0}\n".format("1" * 32))
        tracked = [
            "Runtime/One.cs",
            "Runtime/Two.cs",
            "Runtime/Two.cs.meta",
            "Runtime.meta",
            "Other",
            "Other.meta",
        ]
        validator = ci.RepositoryValidator(self.root, tracked)
        validator.check_meta_and_guids()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("missing-meta", codes)
        self.assertIn("duplicate-guid", codes)

    def test_meta_checker_detects_orphan_meta(self):
        self.write("Runtime/Deleted.cs.meta", "fileFormatVersion: 2\nguid: {0}\n".format("1" * 32))
        (self.root / "Empty").mkdir()
        self.write("Empty.meta", "fileFormatVersion: 2\nguid: {0}\n".format("2" * 32))
        validator = ci.RepositoryValidator(
            self.root, ["Runtime/Deleted.cs.meta", "Empty.meta"]
        )
        validator.check_meta_and_guids()
        codes = [finding.code for finding in validator.findings]
        self.assertEqual(2, codes.count("orphan-meta"))

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

    def test_generated_directory_names_are_forbidden_only_at_package_root(self):
        self.write("Library/cache.bin")
        self.write("Runtime/Library/Foo.cs")
        tracked = ["Library/cache.bin", "Runtime/Library/Foo.cs"]
        validator = ci.RepositoryValidator(self.root, tracked)
        validator.check_paths_and_forbidden_files()
        forbidden = [
            finding.path
            for finding in validator.findings
            if finding.code == "forbidden-root"
        ]
        self.assertEqual(["Library/cache.bin"], forbidden)

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

    def test_non_editor_assembly_outside_runtime_cannot_use_editor_dependencies(self):
        self.write_json(
            "Tests/Runtime/Main.asmdef",
            {
                "name": "CoCoFlow.Tests.Runtime.Main",
                "references": ["CoCoFlow.Tools"],
                "includePlatforms": [],
                "precompiledReferences": ["UnityEditor.CoreModule.dll"],
            },
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
            self.root, ["Tests/Runtime/Main.asmdef", "Tools/Tools.asmdef"]
        )
        validator.check_assemblies()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("runtime-editor-reference", codes)
        self.assertIn("runtime-editor-precompiled", codes)

    def test_external_guid_reference_is_not_a_false_error(self):
        self.write_json(
            "Runtime/Main.asmdef",
            {"name": "CoCoFlow.Runtime.Main", "references": ["GUID:" + "a" * 32]},
        )
        validator = ci.RepositoryValidator(self.root, ["Runtime/Main.asmdef"])
        validator.check_assemblies()
        self.assertEqual([], validator.findings)

    def test_assembly_reference_syntax_and_format_are_validated(self):
        self.write_json(
            "Runtime/Main.asmdef",
            {
                "name": "CoCoFlow.Runtime.Main",
                "references": ["", "GUID:not-a-guid", "GUID:" + "a" * 32, "External.Name"],
            },
        )
        validator = ci.RepositoryValidator(self.root, ["Runtime/Main.asmdef"])
        validator.check_assemblies()
        codes = [finding.code for finding in validator.findings]
        self.assertEqual(2, codes.count("assembly-reference-syntax"))
        self.assertIn("asmdef-mixed-reference-format", codes)

    def test_local_assembly_prefix_is_matched_case_insensitively(self):
        self.write_json(
            "Runtime/Main.asmdef",
            {
                "name": "CoCoFlow.Runtime.Main",
                "references": ["cocoflow.Runtime.Core"],
            },
        )
        self.write_json(
            "Runtime/Core.asmdef",
            {"name": "CoCoFlow.Runtime.Core", "references": []},
        )
        validator = ci.RepositoryValidator(
            self.root, ["Runtime/Main.asmdef", "Runtime/Core.asmdef"]
        )
        validator.check_assemblies()
        self.assertIn(
            "asmdef-reference-case",
            {finding.code for finding in validator.findings},
        )

    def test_package_unity_requires_major_minor(self):
        self.write_json(
            "package.json",
            {
                "name": ci.PACKAGE_NAME,
                "version": "0.4.0-pre.15",
                "unity": "6000.3.20",
            },
        )
        self.write_json("ValidationExceptions.json", {})
        validator = ci.RepositoryValidator(
            self.root, ["package.json", "ValidationExceptions.json"]
        )
        validator.check_package_metadata()
        self.assertIn("package-unity", {finding.code for finding in validator.findings})

    def test_missing_diff_base_uses_selected_severity(self):
        error_validator = ci.RepositoryValidator(self.root, [])
        error_validator.check_diff("f" * 40, "error")
        self.assertEqual("error", error_validator.findings[0].level)

        notice_validator = ci.RepositoryValidator(self.root, [])
        notice_validator.check_diff("f" * 40, "notice")
        self.assertEqual("notice", notice_validator.findings[0].level)

    def test_merge_base_diff_ignores_changes_only_on_updated_base(self):
        def git(*arguments):
            result = subprocess.run(
                ["git"] + list(arguments),
                cwd=str(self.root),
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

        git("init", "-b", "base")
        git("config", "user.email", "ci-test@example.invalid")
        git("config", "user.name", "CoCoFlow CI Test")
        git("config", "core.autocrlf", "false")
        (self.root / "shared.txt").write_bytes(b"trailing space \n")
        git("add", "shared.txt")
        git("commit", "-m", "base")
        git("checkout", "-b", "feature")
        (self.root / "feature.txt").write_bytes(b"feature\n")
        git("add", "feature.txt")
        git("commit", "-m", "feature")
        git("checkout", "base")
        (self.root / "shared.txt").write_bytes(b"clean\n")
        git("add", "shared.txt")
        git("commit", "-m", "upstream fix")
        git("checkout", "feature")

        direct = ci.RepositoryValidator(self.root, [])
        direct.check_diff("base", "error", "direct")
        self.assertIn("diff-check", {finding.code for finding in direct.findings})

        merge_base = ci.RepositoryValidator(self.root, [])
        merge_base.check_diff("base", "error", "merge-base")
        self.assertEqual([], merge_base.findings)


class UnityResultTests(unittest.TestCase):
    def test_missing_or_malformed_result_is_invalid(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            self.assertFalse(ci.parse_unity_test_result(result)["valid"])
            result.write_text("<test-run", encoding="utf-8")
            self.assertFalse(ci.parse_unity_test_result(result)["valid"])

    def test_valid_zero_debt_result_is_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            result.write_text(
                '<test-run total="2" passed="2" failed="0" inconclusive="0" skipped="0" result="Passed" />',
                encoding="utf-8",
            )
            parsed = ci.parse_unity_test_result(result)
            self.assertTrue(parsed["valid"])
            self.assertEqual(2, parsed["total"])
            self.assertTrue(ci.unity_test_passed(parsed))

    def test_failed_and_inconclusive_results_do_not_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            fixtures = (
                '<test-run total="2" passed="1" failed="1" inconclusive="0" skipped="0" result="Failed(Child)" />',
                '<test-run total="2" passed="1" failed="0" inconclusive="1" skipped="0" result="Passed" />',
            )
            for contents in fixtures:
                with self.subTest(contents=contents):
                    result.write_text(contents, encoding="utf-8")
                    parsed = ci.parse_unity_test_result(result)
                    self.assertTrue(parsed["valid"])
                    self.assertFalse(ci.unity_test_passed(parsed))

    def test_invalid_result_shape_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = Path(temporary) / "result.xml"
            fixtures = (
                '<not-a-test-run total="1" passed="1" failed="0" inconclusive="0" skipped="0" result="Passed" />',
                '<test-run total="1" passed="1" result="Passed" />',
                '<test-run total="2" passed="1" failed="0" inconclusive="0" skipped="0" result="Passed" />',
            )
            for contents in fixtures:
                with self.subTest(contents=contents):
                    result.write_text(contents, encoding="utf-8")
                    self.assertFalse(ci.parse_unity_test_result(result)["valid"])

    def test_unity_result_command_returns_nonzero_for_missing_or_debt(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.assertEqual(1, ci.main(["--root", str(root), "unity-result", "missing.xml"]))
            result = root / "failed.xml"
            result.write_text(
                '<test-run total="1" passed="0" failed="1" inconclusive="0" skipped="0" result="Failed" />',
                encoding="utf-8",
            )
            self.assertEqual(1, ci.main(["--root", str(root), "unity-result", "failed.xml"]))

    def test_unity_result_command_accepts_zero_debt_result(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result = root / "passed.xml"
            result.write_text(
                '<test-run total="1" passed="1" failed="0" inconclusive="0" skipped="0" result="Passed" />',
                encoding="utf-8",
            )
            self.assertEqual(0, ci.main(["--root", str(root), "unity-result", "passed.xml"]))


if __name__ == "__main__":
    unittest.main()
