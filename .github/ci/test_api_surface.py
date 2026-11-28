import unittest
import xml.etree.ElementTree as ET

from check_api_surface import compare, documentation_error


class ApiContractTests(unittest.TestCase):
    def test_new_missing_and_changed_api_entries_name_the_symbol(self):
        key = ("Core", "M:Core.Service.Start(System.Int32)")
        overload = ("Core", "M:Core.Service.Start")
        approved = {key: ["public void Start(int mode = 0)"]}
        self.assertFalse(compare("Core", approved, approved.copy()))
        self.assertIn("missing", compare("Core", approved, {})[0])
        self.assertIn("added", compare("Core", approved, {**approved, overload: ["public void Start()"]})[0])
        for signature in ("protected void Start(int mode = 0)", "public void Start(int mode = 1)"):
            self.assertIn("changed", compare("Core", approved, {key: [signature]})[0])

    def test_each_overload_requires_a_real_or_resolved_summary(self):
        docs = {
            "M:Start": ET.fromstring("<member><summary>Starts the service.</summary></member>"),
            "M:Start(System.Int32)": ET.fromstring('<member><inheritdoc cref="M:Start" /></member>'),
            "blank": ET.fromstring("<member><summary> </summary></member>"),
            "todo": ET.fromstring("<member><summary>TODO: write later.</summary></member>"),
            "unknown": ET.fromstring('<member><inheritdoc cref="unavailable" /></member>'),
            "implicit": ET.fromstring("<member><inheritdoc /></member>"),
        }
        for member in ("M:Start", "M:Start(System.Int32)", "implicit"):
            self.assertIsNone(documentation_error(member, docs, {"implicit": ["M:Start"]}))
        for member in ("M:Start(System.String)", "blank", "todo", "unknown", "implicit"):
            self.assertIsNotNone(documentation_error(member, docs, {}))
