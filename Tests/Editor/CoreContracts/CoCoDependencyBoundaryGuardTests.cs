using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.Tests
{
    /// <summary>
    /// Plan PR15.10 deliverable 7: asmdef structural boundary guard.
    /// Asserts the frozen dependency contract on every asmdef of the package:
    /// exact optional-dependency triples, no Editor/test/sample leakage into
    /// runtime, no raw Addressables reference in UI/Map, and no Addressables
    /// hard dependency in package.json. Pure-rule core is negative-testable.
    /// </summary>
    public sealed class CoCoDependencyBoundaryGuardTests
    {
        [Test]
        public void EveryAsmdefInPackageSatisfiesDependencyBoundaryRules()
        {
            List<DependencyAsmdefRules.AsmdefInfo> infos = LoadPackageAsmdefs(out string packageRoot);
            Assert.IsNotEmpty(infos, "No asmdef files found under the package root.");

            var pkg = DependencyAsmdefRules.ParsePackageJson(Path.Combine(packageRoot, "package.json"));

            List<string> violations = new List<string>();
            foreach (DependencyAsmdefRules.AsmdefInfo info in infos)
            {
                violations.AddRange(DependencyAsmdefRules.CollectViolations(info, pkg));
            }

            violations.AddRange(DependencyAsmdefRules.CollectPackageJsonViolations(pkg));

            Assert.IsEmpty(
                violations,
                "Dependency boundary violations:\n" + string.Join("\n", violations));
        }

        [Test]
        public void RuntimeAndEditorAsmdefsNeverReferenceTestOrSampleAssemblies()
        {
            List<DependencyAsmdefRules.AsmdefInfo> infos = LoadPackageAsmdefs(out _);

            List<string> violations = new List<string>();
            foreach (DependencyAsmdefRules.AsmdefInfo info in infos)
            {
                string zone = DependencyAsmdefRules.ZoneOf(info.RelativePath);
                if (zone != "runtime" && zone != "editor")
                    continue;

                foreach (string reference in info.References)
                {
                    if (DependencyAsmdefRules.IsTestOrSampleAssembly(reference))
                        violations.Add(info.RelativePath + " references test/sample assembly " + reference + " (test-only/sample-only isolation).");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        // ---- Negative cases: the rule core must reject wrong triples ----

        [Test]
        public void RuleCoreRejectsWrongUniTaskVersionDefinesTriple()
        {
            string wrongPackage = CreateAsmdefJson(
                references: "\"UniTask\"",
                constraints: "\"COCOFLOW_UNITASK_SUPPORT\"",
                versionDefines: "{\"name\":\"com.cysharp.unitask.typo\",\"expression\":\"[2.5.11,3.0.0)\",\"define\":\"COCOFLOW_UNITASK_SUPPORT\"}");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(wrongPackage, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.Any(e => e.Contains("com.cysharp.unitask")), "Wrong package name must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsWrongUniTaskRange()
        {
            string wrongRange = CreateAsmdefJson(
                references: "\"UniTask\"",
                constraints: "\"COCOFLOW_UNITASK_SUPPORT\"",
                versionDefines: "{\"name\":\"com.cysharp.unitask\",\"expression\":\"[0.0.1,9.0.0)\",\"define\":\"COCOFLOW_UNITASK_SUPPORT\"}");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(wrongRange, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("[2.5.11,3.0.0)")), "Wrong expression range must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsWrongUniTaskDefineName()
        {
            string wrongDefine = CreateAsmdefJson(
                references: "\"UniTask\"",
                constraints: "\"COCOFLOW_UNITASK_SUPPORT\"",
                versionDefines: "{\"name\":\"com.cysharp.unitask\",\"expression\":\"[2.5.11,3.0.0)\",\"define\":\"COCOFLOW_UNITASK_WRONG\"}");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(wrongDefine, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("COCOFLOW_UNITASK_SUPPORT")), "Wrong define name must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsDuplicateVersionDefinesEntries()
        {
            string duplicated = CreateAsmdefJson(
                references: "\"UniTask\"",
                constraints: "\"COCOFLOW_UNITASK_SUPPORT\"",
                versionDefines:
                    "{\"name\":\"com.cysharp.unitask\",\"expression\":\"[2.5.11,3.0.0)\",\"define\":\"COCOFLOW_UNITASK_SUPPORT\"}," +
                    "{\"name\":\"com.cysharp.unitask\",\"expression\":\"[2.5.11,3.0.0)\",\"define\":\"COCOFLOW_UNITASK_SUPPORT\"}");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(duplicated, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("duplicate")), "Duplicate versionDefines entries must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsUniTaskReferenceWithoutConstraintOrVersionDefines()
        {
            string bare = CreateAsmdefJson(references: "\"UniTask\"", constraints: "", versionDefines: "");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(bare, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("COCOFLOW_UNITASK_SUPPORT")), "UniTask reference without the constraint must be rejected.");
            Assert.IsTrue(errors.Any(e => e.Contains("[2.5.11,3.0.0)")), "UniTask reference without versionDefines must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsRuntimeAsmdefReferencingEditorAssembly()
        {
            string runtimeToEditor = CreateAsmdefJson(references: "\"CoCoFlow.Editor.Core\"", constraints: "", versionDefines: "");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(runtimeToEditor, "Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("CoCoFlow.Editor")), "Runtime must not reference Editor assemblies.");
        }

        [Test]
        public void RuleCoreRejectsRawAddressablesReferenceInUiOrMapModule()
        {
            string rawAddressables = CreateAsmdefJson(
                name: "CoCoFlow.Runtime.Modules.UI",
                references: "\"Unity.Addressables\"",
                constraints: "",
                versionDefines: "");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(rawAddressables, "Runtime/Modules/UI/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("raw Addressables")), "UI/Map must not carry raw Addressables references.");
        }

        [Test]
        public void RuleCoreRejectsBackendTripleOnTransitiveResourceManagerReference()
        {
            string transitiveWithTriple = CreateAsmdefJson(
                name: "CoCoFlow.Runtime.Modules.Localization.UI",
                references: "\"Unity.ResourceManager\"",
                constraints: "",
                versionDefines: "{\"name\":\"com.unity.addressables\",\"expression\":\"[2.9.1,3.0.0)\",\"define\":\"COCOFLOW_ADDRESSABLES_2_9_OR_NEWER\"}");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(transitiveWithTriple, "Runtime/Modules/Localization/UI/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("transitive")), "Transitive ResourceManager references must not masquerade as optional-backend version locks.");
        }

        [Test]
        public void RuleCoreRejectsTestAsmdefWithoutOfficialIsolation()
        {
            string bare = CreateAsmdefJson(references: "", constraints: "", versionDefines: "");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(bare, "Tests/Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("TestAssemblies")), "Missing TestAssemblies flag must be rejected (VR-1 F1).");
            Assert.IsTrue(errors.Any(e => e.Contains("UNITY_INCLUDE_TESTS")), "Missing UNITY_INCLUDE_TESTS constraint must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsExplicitTestrunnerReferencesOnTestAsmdef()
        {
            string explicitRefs = CreateAsmdefJson(
                references: "\"UnityEngine.TestRunner\"",
                constraints: "",
                versionDefines: "");
            List<string> errors = DependencyAsmdefRules.CollectViolations(
                DependencyAsmdefRules.Parse(explicitRefs, "Tests/Runtime/X/Y.asmdef"),
                null);
            Assert.IsTrue(errors.Any(e => e.Contains("explicitly")), "Explicit testrunner references on test asmdefs must be rejected.");
        }

        [Test]
        public void RuleCoreRejectsAddressablesHardDependencyInPackageJson()
        {
            var pkg = new DependencyAsmdefRules.PackageJsonInfo
            {
                RawText = "{\"dependencies\":{\"com.unity.addressables\":\"2.9.1\"}}",
                Dependencies = new Dictionary<string, string> { { "com.unity.addressables", "2.9.1" } },
            };
            List<string> errors = DependencyAsmdefRules.CollectPackageJsonViolations(pkg);
            Assert.IsTrue(errors.Any(e => e.Contains("com.unity.addressables")), "package.json must not hard-depend on Addressables.");
        }

        private static string CreateAsmdefJson(string references, string constraints, string versionDefines, string name = "X")
        {
            var builder = new StringBuilder("{\"name\":\"");
            builder.Append(name);
            builder.Append("\",\"references\":[");
            builder.Append(references);
            builder.Append("],\"defineConstraints\":[");
            builder.Append(constraints);
            builder.Append("],\"versionDefines\":[");
            builder.Append(versionDefines);
            builder.Append("]}");
            return builder.ToString();
        }

        private static List<DependencyAsmdefRules.AsmdefInfo> LoadPackageAsmdefs(out string packageRoot)
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(CoCoDependencyBoundaryGuardTests).Assembly);
            Assert.IsNotNull(packageInfo, "PackageInfo could not be resolved for the test assembly.");
            packageRoot = packageInfo.resolvedPath;

            var result = new List<DependencyAsmdefRules.AsmdefInfo>();
            foreach (string file in Directory.EnumerateFiles(packageRoot, "*.asmdef", SearchOption.AllDirectories))
            {
                string relative = file.Substring(packageRoot.Length + 1).Replace('\\', '/');
                if (DependencyAsmdefRules.IsOutsidePackageScan(relative))
                    continue;

                result.Add(DependencyAsmdefRules.Parse(File.ReadAllText(file), relative));
            }

            return result;
        }
    }

    /// <summary>Minimal JSON reader for asmdef/package.json parsing in tests (no engine JSON dependency).</summary>
    internal static class MiniJson
    {
        internal static object Parse(string text)
        {
            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
                throw new FormatException("Trailing characters at " + index + ".");
            return value;
        }

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
                throw new FormatException("Unexpected end of JSON.");

            char c = text[index];
            switch (c)
            {
                case '{': return ParseObject(text, ref index);
                case '[': return ParseArray(text, ref index);
                case '"': return ParseString(text, ref index);
                case 't': Expect(text, ref index, "true"); return true;
                case 'f': Expect(text, ref index, "false"); return false;
                case 'n': Expect(text, ref index, "null"); return null;
                default: return ParseNumber(text, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            var result = new Dictionary<string, object>();
            index++; // {
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}') { index++; return result; }

            while (true)
            {
                SkipWhitespace(text, ref index);
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (text[index] != ':') throw new FormatException("Expected ':' at " + index + ".");
                index++;
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("Unterminated object.");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return result; }
                throw new FormatException("Expected ',' or '}' at " + index + ".");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var result = new List<object>();
            index++; // [
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']') { index++; return result; }

            while (true)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("Unterminated array.");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return result; }
                throw new FormatException("Expected ',' or ']' at " + index + ".");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            if (text[index] != '"') throw new FormatException("Expected string at " + index + ".");
            index++;
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return builder.ToString();
                if (c == '\\')
                {
                    if (index >= text.Length) throw new FormatException("Unterminated escape.");
                    char escaped = text[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append((char)Convert.ToInt32(text.Substring(index, 4), 16));
                            index += 4;
                            break;
                        default: throw new FormatException("Unknown escape " + escaped + ".");
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw new FormatException("Unterminated string.");
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && "-+.eE0123456789".IndexOf(text[index]) >= 0)
                index++;
            if (double.TryParse(text.Substring(start, index - start), out double value))
                return value;
            throw new FormatException("Invalid number at " + start + ".");
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || text.Substring(index, literal.Length) != literal)
                throw new FormatException("Expected '" + literal + "' at " + index + ".");
            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }
    }

    /// <summary>Pure rule core shared by the guard tests and negative cases.</summary>
    internal static class DependencyAsmdefRules
    {
        internal const string UniTaskAssembly = "UniTask";
        internal const string UniTaskDefine = "COCOFLOW_UNITASK_SUPPORT";
        internal const string UniTaskPackage = "com.cysharp.unitask";
        internal const string UniTaskRange = "[2.5.11,3.0.0)";

        internal const string DotweenDefine = "COCOFLOW_DOTWEEN_SUPPORT";

        internal const string AddressablesDefine = "COCOFLOW_ADDRESSABLES_2_9_OR_NEWER";
        internal const string AddressablesPackage = "com.unity.addressables";
        internal const string AddressablesRange = "[2.9.1,3.0.0)";

        internal sealed class AsmdefInfo
        {
            internal string RelativePath;
            internal string Name;
            internal string[] References = Array.Empty<string>();
            internal string[] DefineConstraints = Array.Empty<string>();
            internal string[] OptionalUnityReferences = Array.Empty<string>();
            internal List<Dictionary<string, string>> VersionDefines = new List<Dictionary<string, string>>();
        }

        internal sealed class PackageJsonInfo
        {
            internal string RawText = string.Empty;
            internal Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        }

        internal static bool IsOutsidePackageScan(string relativePath)
        {
            return relativePath.StartsWith(".ci-artifacts/", StringComparison.Ordinal) ||
                   relativePath.StartsWith("Library/", StringComparison.Ordinal) ||
                   relativePath.StartsWith("Temp/", StringComparison.Ordinal) ||
                   relativePath.Contains("/Library/") ||
                   relativePath.Contains("/Temp/");
        }

        internal static string ZoneOf(string relativePath)
        {
            if (relativePath.StartsWith("Runtime/", StringComparison.Ordinal))
                return "runtime";
            if (relativePath.StartsWith("Editor/", StringComparison.Ordinal))
                return "editor";
            if (relativePath.StartsWith("Tests/", StringComparison.Ordinal))
                return "tests";
            if (relativePath.StartsWith("Samples~/", StringComparison.Ordinal))
                return relativePath.Contains("/Tests/", StringComparison.Ordinal) ? "tests" : "samples";
            return "other";
        }

        internal static bool IsTestOrSampleAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("CoCoFlow.Tests.", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("CoCoFlow.Samples.", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("CoCoFlow.Fixtures.", StringComparison.Ordinal);
        }

        internal static AsmdefInfo Parse(string json, string relativePath)
        {
            var info = new AsmdefInfo { RelativePath = relativePath };
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
                return info;

            info.Name = GetStringLength(root, "name");
            info.References = GetStringArray(root, "references");
            info.DefineConstraints = GetStringArray(root, "defineConstraints");
            info.OptionalUnityReferences = GetStringArray(root, "optionalUnityReferences");

            if (root.TryGetValue("versionDefines", out object rawDefines) && rawDefines is List<object> defines)
            {
                foreach (object entry in defines)
                {
                    if (entry is Dictionary<string, object> define)
                    {
                        info.VersionDefines.Add(new Dictionary<string, string>
                        {
                            ["name"] = define.TryGetValue("name", out object n) ? n as string : null,
                            ["expression"] = define.TryGetValue("expression", out object e) ? e as string : null,
                            ["define"] = define.TryGetValue("define", out object d) ? d as string : null,
                        });
                    }
                }
            }

            return info;
        }

        internal static PackageJsonInfo ParsePackageJson(string path)
        {
            var result = new PackageJsonInfo();
            if (!File.Exists(path))
                return result;

            result.RawText = File.ReadAllText(path);
            var root = MiniJson.Parse(result.RawText) as Dictionary<string, object>;
            if (root != null && root.TryGetValue("dependencies", out object deps) && deps is Dictionary<string, object> dict)
            {
                foreach (KeyValuePair<string, object> pair in dict)
                    result.Dependencies[pair.Key] = pair.Value as string;
            }

            return result;
        }

        internal static List<string> CollectViolations(AsmdefInfo info, PackageJsonInfo packageJson)
        {
            var errors = new List<string>();

            // Duplicate versionDefines entries for the same define are forbidden.
            foreach (IGrouping<string, Dictionary<string, string>> group in info.VersionDefines.GroupBy(v => v.GetValueOrDefault("define")))
            {
                if (!string.IsNullOrEmpty(group.Key) && group.Count() > 1)
                    errors.Add(info.RelativePath + ": duplicate versionDefines entries for define " + group.Key + ".");
            }

            if (info.References.Contains(UniTaskAssembly))
            {
                if (!info.DefineConstraints.Contains(UniTaskDefine))
                    errors.Add(info.RelativePath + ": references UniTask without defineConstraint " + UniTaskDefine + ".");

                if (!HasExactTriple(info, UniTaskPackage, UniTaskRange, UniTaskDefine))
                    errors.Add(info.RelativePath + ": references UniTask without exact versionDefines triple (" +
                               UniTaskPackage + " / " + UniTaskRange + " / " + UniTaskDefine + ").");
            }

            if (info.References.Contains("Unity.Addressables") || info.References.Contains("Unity.ResourceManager"))
            {
                bool isOptionalAddressablesBackend =
                    info.RelativePath.Contains("Content/Addressables/", StringComparison.Ordinal) ||
                    info.RelativePath.Contains("Map/Addressables/", StringComparison.Ordinal) ||
                    (info.Name != null && info.Name.Contains("Content.Addressables"));

                if (isOptionalAddressablesBackend)
                {
                    if (!HasExactTriple(info, AddressablesPackage, AddressablesRange, AddressablesDefine))
                        errors.Add(info.RelativePath + ": optional Addressables backend without exact versionDefines triple (" +
                                   AddressablesPackage + " / " + AddressablesRange + " / " + AddressablesDefine + ").");
                }
                else if (HasAnyTripleFor(info, AddressablesDefine))
                {
                    // 传递性引用（如 Localization→ResourceManager 恒在）不得伪装成可选后端锁版：
                    // 否则 resolved 版本低于 2.9.1 时会错误禁用整组程序集。
                    errors.Add(info.RelativePath + ": transitive Addressables/ResourceManager reference must not carry the optional-backend versionDefines triple (existence is guaranteed by hard dependencies).");
                }
            }

            if (info.References.Contains("DOTween") || info.References.Contains("DOTween.Modules"))
            {
                if (!info.DefineConstraints.Contains(DotweenDefine))
                    errors.Add(info.RelativePath + ": references DOTween without defineConstraint " + DotweenDefine + ".");
            }

            string zone = ZoneOf(info.RelativePath);
            if (zone == "tests")
            {
                // VR-1 F1 guard: test assemblies must use the official isolation
                // (TestAssemblies flag + UNITY_INCLUDE_TESTS constraint, no explicit
                // testrunner references) so consumer Player builds stay clean while
                // UTF hosts (testables / runTests) still compile them.
                if (!info.OptionalUnityReferences.Contains("TestAssemblies"))
                    errors.Add(info.RelativePath + ": test assembly missing optionalUnityReferences TestAssemblies (leaks into consumer Player builds - VR-1 F1).");
                if (!info.DefineConstraints.Contains("UNITY_INCLUDE_TESTS"))
                    errors.Add(info.RelativePath + ": test assembly missing UNITY_INCLUDE_TESTS defineConstraint.");
                if (info.References.Contains("UnityEditor.TestRunner") || info.References.Contains("UnityEngine.TestRunner"))
                    errors.Add(info.RelativePath + ": test assembly must not reference testrunner assemblies explicitly (conflicts with TestAssemblies auto-injection).");
            }

            if (zone == "runtime")
            {
                foreach (string reference in info.References)
                {
                    if (reference.StartsWith("CoCoFlow.Editor", StringComparison.Ordinal))
                        errors.Add(info.RelativePath + ": runtime assembly references Editor assembly " + reference + ".");
                }
            }

            if (zone == "runtime" || zone == "editor")
            {
                foreach (string reference in info.References)
                {
                    if (IsTestOrSampleAssembly(reference))
                        errors.Add(info.RelativePath + ": package assembly references test/sample assembly " + reference + ".");
                }

                if (info.Name != null &&
                    (info.Name.Contains("Modules.UI") || info.Name.Contains("Modules.Map")) &&
                    (info.References.Contains("Unity.Addressables") || info.References.Contains("Unity.ResourceManager")))
                {
                    errors.Add(info.RelativePath + ": UI/Map module carries a raw Addressables reference (must go through Content.Addressables).");
                }
            }

            return errors;
        }

        internal static List<string> CollectPackageJsonViolations(PackageJsonInfo packageJson)
        {
            var errors = new List<string>();
            if (packageJson.Dependencies.ContainsKey(AddressablesPackage))
                errors.Add("package.json hard-depends on " + AddressablesPackage + " (frozen contract: optional only).");
            if (packageJson.Dependencies.ContainsKey(UniTaskPackage))
                errors.Add("package.json hard-depends on " + UniTaskPackage + " (optional only).");
            return errors;
        }

        private static bool HasExactTriple(AsmdefInfo info, string package, string range, string define)
        {
            int matches = info.VersionDefines.Count(v =>
                v.GetValueOrDefault("name") == package &&
                v.GetValueOrDefault("expression") == range &&
                v.GetValueOrDefault("define") == define);
            return matches == 1;
        }

        private static bool HasAnyTripleFor(AsmdefInfo info, string define)
        {
            return info.VersionDefines.Any(v => v.GetValueOrDefault("define") == define);
        }

        private static string GetStringLength(Dictionary<string, object> root, string key)
        {
            return root.TryGetValue(key, out object value) ? value as string : null;
        }

        private static string[] GetStringArray(Dictionary<string, object> root, string key)
        {
            if (root.TryGetValue(key, out object value) && value is List<object> list)
                return list.OfType<string>().ToArray();
            return Array.Empty<string>();
        }
    }
}
