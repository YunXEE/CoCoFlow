import json
import tempfile
import unittest
from pathlib import Path

import cocoflow_ci as ci


class TemporaryRepositoryTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.policy = {
            "schemaVersion": 1,
            "packageName": "com.yunxee.cocoflow",
            "packageMinimum": {
                "target": "6000.3",
                "owner": "later",
            },
            "maxTrackedFileBytes": 1024 * 1024,
            "forbiddenRootNames": ["Library", "Obj"],
            "forbiddenExtensions": [".dll"],
        }

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, relative, content):
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

    def test_meta_checker_detects_missing_meta_and_duplicate_guid(self):
        self.write("Runtime/One.cs", "class One {}")
        self.write("Runtime/Two.cs", "class Two {}")
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
        validator = ci.RepositoryValidator(self.root, self.policy, tracked)
        validator.check_meta_and_guids()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("missing-meta", codes)
        self.assertIn("duplicate-guid", codes)

    def test_path_checker_detects_case_collision_and_forbidden_files(self):
        self.write("Runtime/Thing.cs", "")
        self.write("runtime/thing.cs", "")
        self.write("Library/cache.bin", "")
        self.write("Runtime/native.dll", "")
        tracked = [
            "Runtime/Thing.cs",
            "runtime/thing.cs",
            "Library/cache.bin",
            "Runtime/native.dll",
        ]
        validator = ci.RepositoryValidator(self.root, self.policy, tracked)
        validator.check_paths_and_forbidden_files()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("case-collision", codes)
        self.assertIn("forbidden-root", codes)
        self.assertIn("forbidden-extension", codes)

    def test_assembly_checker_rejects_runtime_to_editor_and_duplicates(self):
        runtime = {
            "name": "CoCoFlow.Runtime.Sample",
            "references": [
                "CoCoFlow.Editor.Sample",
                "CoCoFlow.Editor.Sample",
            ],
        }
        editor = {
            "name": "CoCoFlow.Editor.Sample",
            "references": [],
            "includePlatforms": ["Editor"],
        }
        self.write_json("Runtime/Sample.asmdef", runtime)
        self.write_json("Editor/Sample.asmdef", editor)
        tracked = ["Runtime/Sample.asmdef", "Editor/Sample.asmdef"]
        validator = ci.RepositoryValidator(self.root, self.policy, tracked)
        validator.check_assemblies()
        codes = {finding.code for finding in validator.findings}
        self.assertIn("runtime-editor-reference", codes)
        self.assertIn("asmdef-duplicate-reference", codes)


class EditorGuardTests(unittest.TestCase):
    def test_guarded_unity_editor_is_accepted(self):
        source = """#if UNITY_EDITOR
using UnityEditor;
#endif
class Fine {}
"""
        self.assertEqual([], ci.find_unguarded_unity_editor(source))

    def test_unguarded_and_or_guard_are_rejected(self):
        source = """using UnityEditor;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
UnityEditor.EditorApplication.isPlaying = false;
#endif
"""
        self.assertEqual([1, 3], ci.find_unguarded_unity_editor(source))

    def test_comments_and_strings_are_ignored(self):
        source = """// UnityEditor
var text = "UnityEditor";
/* UnityEditor */
"""
        self.assertEqual([], ci.find_unguarded_unity_editor(source))


class ReleasePolicyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.policy = {"packageName": "com.yunxee.cocoflow"}

    def tearDown(self):
        self.temporary.cleanup()

    def write_release(self, version, changelog=True, prerelease_exception=False):
        (self.root / "package.json").write_text(
            json.dumps({"name": "com.yunxee.cocoflow", "version": version}),
            encoding="utf-8",
        )
        heading = "## [{0}] - 2026-07-29\n".format(version) if changelog else "# Changes\n"
        (self.root / "CHANGELOG.md").write_text(heading, encoding="utf-8")
        message = "Version tag must not have a pre-release tag" if prerelease_exception else "Company name"
        (self.root / "ValidationExceptions.json").write_text(
            json.dumps(
                {
                    "ErrorExceptions": [
                        {
                            "PackageVersion": version,
                            "ExceptionMessage": message,
                        }
                    ],
                    "WarningExceptions": [],
                }
            ),
            encoding="utf-8",
        )

    def test_valid_dev_release_passes(self):
        self.write_release("0.4.0")
        findings = ci.release_findings(
            self.root,
            self.policy,
            "dev/0.4.0",
            "master",
            "YunXEE/CoCoFlow",
            "YunXEE/CoCoFlow",
            set(),
        )
        self.assertEqual([], findings)

    def test_prerelease_missing_changelog_and_tag_collision_fail(self):
        self.write_release("0.4.0-pre.15", changelog=False, prerelease_exception=True)
        findings = ci.release_findings(
            self.root,
            self.policy,
            "dev/0.4.0",
            "master",
            existing_tags={"v0.4.0-pre.15"},
        )
        codes = {finding.code for finding in findings}
        self.assertIn("release-version", codes)
        self.assertIn("release-version-branch", codes)
        self.assertIn("release-changelog", codes)
        self.assertIn("release-tag", codes)
        self.assertIn("release-prerelease-exception", codes)

    def test_fork_and_invalid_head_fail(self):
        self.write_release("0.4.0")
        findings = ci.release_findings(
            self.root,
            self.policy,
            "feature/release",
            "master",
            "someone/fork",
            "YunXEE/CoCoFlow",
            set(),
        )
        codes = {finding.code for finding in findings}
        self.assertIn("release-fork", codes)
        self.assertIn("release-head", codes)


class UnityHarnessTests(unittest.TestCase):
    def test_manifest_uses_file_uri_and_testables(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "package"
            host = Path(temporary) / "host"
            root.mkdir()
            ci.create_clean_host(host, root, "com.yunxee.cocoflow", "6000.3.20f1")
            manifest = json.loads(
                (host / "Packages/manifest.json").read_text(encoding="utf-8")
            )
            self.assertEqual(
                ["com.yunxee.cocoflow"],
                manifest["testables"],
            )
            package_uri = manifest["dependencies"]["com.yunxee.cocoflow"]
            self.assertTrue(package_uri.startswith("file:"))
            self.assertNotIn("\\", package_uri)

    def test_test_command_does_not_add_quit(self):
        command = ci.unity_command(
            Path("/Unity"),
            Path("/host"),
            Path("/log"),
            "editmode",
            Path("/result.xml"),
        )
        self.assertIn("-runTests", command)
        self.assertNotIn("-quit", command)


if __name__ == "__main__":
    unittest.main()
