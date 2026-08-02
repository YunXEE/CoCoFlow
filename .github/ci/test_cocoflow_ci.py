import contextlib
import io
import tempfile
import unittest
from pathlib import Path

import cocoflow_ci as ci


class UnityResultTests(unittest.TestCase):
    def parse(self, contents):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "result.xml"
            path.write_text(contents, encoding="utf-8")
            return ci.parse_unity_test_result(path)

    def test_missing_malformed_and_wrong_root_are_invalid(self):
        with tempfile.TemporaryDirectory() as temporary:
            missing = Path(temporary) / "missing.xml"
            self.assertFalse(ci.parse_unity_test_result(missing)["valid"])

        self.assertFalse(self.parse("<test-run")["valid"])
        self.assertFalse(
            self.parse(
                '<suite total="1" passed="1" failed="0" '
                'inconclusive="0" skipped="0" result="Passed" />'
            )["valid"]
        )

    def test_missing_negative_mismatched_and_zero_counts_are_invalid(self):
        fixtures = (
            '<test-run total="1" passed="1" result="Passed" />',
            '<test-run total="1" passed="1" failed="-1" '
            'inconclusive="0" skipped="1" result="Passed" />',
            '<test-run total="2" passed="1" failed="0" '
            'inconclusive="0" skipped="0" result="Passed" />',
            '<test-run total="0" passed="0" failed="0" '
            'inconclusive="0" skipped="0" result="Passed" />',
        )
        for contents in fixtures:
            with self.subTest(contents=contents):
                self.assertFalse(self.parse(contents)["valid"])

    def test_valid_passed_result_is_accepted(self):
        parsed = self.parse(
            '<test-run total="2" passed="2" failed="0" '
            'inconclusive="0" skipped="0" result="Passed" />'
        )
        self.assertTrue(parsed["valid"])
        self.assertTrue(ci.unity_test_passed(parsed))

    def test_failed_and_inconclusive_results_do_not_pass(self):
        fixtures = (
            '<test-run total="2" passed="1" failed="1" '
            'inconclusive="0" skipped="0" result="Failed" />',
            '<test-run total="2" passed="1" failed="0" '
            'inconclusive="1" skipped="0" result="Passed" />',
        )
        for contents in fixtures:
            with self.subTest(contents=contents):
                parsed = self.parse(contents)
                self.assertTrue(parsed["valid"])
                self.assertFalse(ci.unity_test_passed(parsed))

    def test_command_exit_code_matches_result(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            passed = root / "passed.xml"
            passed.write_text(
                '<test-run total="1" passed="1" failed="0" '
                'inconclusive="0" skipped="0" result="Passed" />',
                encoding="utf-8",
            )
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(0, ci.main(["unity-result", str(passed)]))
                self.assertEqual(1, ci.main(["unity-result", str(root / "missing.xml")]))


if __name__ == "__main__":
    unittest.main()
