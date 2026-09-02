using NUnit.Framework;

namespace CoCoFlow.Editor.Core.Tests
{
    /// <summary>
    /// CoCoSetupJson / CoCoSetupDependencyActions 纯逻辑单测（方案 v4 §2.4）：
    /// JsonParser 往返、Newtonsoft 推荐项增改、OpenUPM scope 移除与空 registry 清理。
    /// 只做内存对象断言，不触碰 Packages/manifest.json。
    /// </summary>
    public sealed class CoCoSetupManifestEditTests
    {
        private static JsonObject Parse(string json)
        {
            return (JsonObject)new JsonParser(json).Parse();
        }

        [Test]
        public void ParserRoundTripsDependenciesAndScopedRegistries()
        {
            const string source =
                "{\n" +
                "  \"dependencies\": {\n" +
                "    \"com.cysharp.unitask\": \"https://github.com/Cysharp/UniTask.git#2.5.11\",\n" +
                "    \"com.unity.nuget.newtonsoft-json\": \"3.2.1\"\n" +
                "  },\n" +
                "  \"scopedRegistries\": [\n" +
                "    {\n" +
                "      \"name\": \"package.openupm.com\",\n" +
                "      \"url\": \"https://package.openupm.com\",\n" +
                "      \"scopes\": [\n" +
                "        \"com.cysharp.unitask\"\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            var root = Parse(source);
            Assert.IsTrue(root.TryGetObject("dependencies", out var dependencies));
            Assert.IsTrue(dependencies.TryGetString(
                "com.cysharp.unitask",
                out var unitask));
            StringAssert.StartsWith("https://github.com/Cysharp/UniTask.git", unitask);

            var serialized = root.ToJson(0);
            var reparsed = (JsonObject)new JsonParser(serialized).Parse();
            Assert.IsTrue(reparsed.TryGetObject("dependencies", out var dependenciesAgain));
            Assert.IsTrue(dependenciesAgain.TryGetString(
                "com.unity.nuget.newtonsoft-json",
                out var newtonsoft));
            Assert.AreEqual("3.2.1", newtonsoft);
        }

        [Test]
        public void ConfigureNewtonsoftAddsMissingRecommendation()
        {
            var root = Parse("{\"dependencies\": {}}");
            var manifest = new ManifestDocument(root);
            var log = new CoCoSetupDependencyActions.MessageCollector();

            var dependencies = CoCoSetupDependencyActions.GetOrCreateObject(
                root,
                "dependencies",
                manifest);
            bool changed = ApplyNewtonsoftRecommendation(dependencies, manifest, log);

            Assert.IsTrue(changed);
            Assert.IsTrue(manifest.Changed);
            Assert.IsTrue(dependencies.TryGetString(
                CoCoFlowUtility.NewtonsoftPackageName,
                out var version));
            Assert.AreEqual(CoCoFlowUtility.NewtonsoftMinimumVersion, version);
            Assert.AreEqual(1, log.Messages.Count);
        }

        [Test]
        public void ConfigureNewtonsoftUpgradesBelowMinimum()
        {
            var root = Parse(
                "{\"dependencies\": {\"com.unity.nuget.newtonsoft-json\": \"3.2.1\"}}");
            var manifest = new ManifestDocument(root);
            var log = new CoCoSetupDependencyActions.MessageCollector();

            var dependencies = CoCoSetupDependencyActions.GetOrCreateObject(
                root,
                "dependencies",
                manifest);
            bool changed = ApplyNewtonsoftRecommendation(dependencies, manifest, log);

            Assert.IsTrue(changed);
            Assert.IsTrue(dependencies.TryGetString(
                CoCoFlowUtility.NewtonsoftPackageName,
                out var version));
            Assert.AreEqual(CoCoFlowUtility.NewtonsoftMinimumVersion, version);
        }

        [Test]
        public void ConfigureNewtonsoftKeepsSatisfiedVersion()
        {
            var satisfied = CoCoFlowUtility.NewtonsoftMinimumVersion;
            var root = Parse(
                "{\"dependencies\": {\"com.unity.nuget.newtonsoft-json\": \"" + satisfied + "\"}}");
            var manifest = new ManifestDocument(root);
            var log = new CoCoSetupDependencyActions.MessageCollector();

            var dependencies = CoCoSetupDependencyActions.GetOrCreateObject(
                root,
                "dependencies",
                manifest);
            bool changed = ApplyNewtonsoftRecommendation(dependencies, manifest, log);

            Assert.IsFalse(changed);
            Assert.IsFalse(manifest.Changed);
            Assert.IsTrue(dependencies.TryGetString(
                CoCoFlowUtility.NewtonsoftPackageName,
                out var version));
            Assert.AreEqual(satisfied, version);
        }

        [Test]
        public void RemoveUniTaskScopeDropsScopeAndEmptyRegistry()
        {
            var root = Parse(
                "{\n" +
                "  \"scopedRegistries\": [\n" +
                "    {\n" +
                "      \"name\": \"package.openupm.com\",\n" +
                "      \"url\": \"https://package.openupm.com\",\n" +
                "      \"scopes\": [\"com.cysharp.unitask\"]\n" +
                "    },\n" +
                "    {\n" +
                "      \"name\": \"package.openupm.com\",\n" +
                "      \"url\": \"https://package.openupm.com\",\n" +
                "      \"scopes\": [\"com.other.pkg\", \"com.cysharp.unitask\"]\n" +
                "    }\n" +
                "  ]\n" +
                "}");
            var manifest = new ManifestDocument(root);
            var log = new CoCoSetupDependencyActions.MessageCollector();

            CoCoSetupDependencyActions.RemoveUniTaskOpenUpmScope(root, manifest, log);

            Assert.IsTrue(manifest.Changed);
            Assert.IsTrue(root.TryGetArray("scopedRegistries", out var registries));
            Assert.AreEqual(1, registries.Items.Count);
            var remaining = (JsonObject)registries.Items[0];
            Assert.IsTrue(remaining.TryGetArray("scopes", out var scopes));
            Assert.AreEqual(1, scopes.Items.Count);
            Assert.AreEqual("com.other.pkg", ((JsonString)scopes.Items[0]).Value);
        }

        [Test]
        public void RemoveUniTaskScopeIgnoresForeignRegistries()
        {
            var root = Parse(
                "{\n" +
                "  \"scopedRegistries\": [\n" +
                "    {\n" +
                "      \"name\": \"example.com\",\n" +
                "      \"url\": \"https://example.com\",\n" +
                "      \"scopes\": [\"com.cysharp.unitask\"]\n" +
                "    }\n" +
                "  ]\n" +
                "}");
            var manifest = new ManifestDocument(root);
            var log = new CoCoSetupDependencyActions.MessageCollector();

            CoCoSetupDependencyActions.RemoveUniTaskOpenUpmScope(root, manifest, log);

            Assert.IsFalse(manifest.Changed);
            Assert.IsTrue(root.TryGetArray("scopedRegistries", out var registries));
            Assert.AreEqual(1, registries.Items.Count);
        }

        [Test]
        public void SemanticVersionLowerOnlyBelowMinimum()
        {
            Assert.IsTrue(CoCoSetupDependencyActions.IsSemanticVersionLower(
                "3.2.1", "3.2.2"));
            Assert.IsFalse(CoCoSetupDependencyActions.IsSemanticVersionLower(
                "3.2.2", "3.2.2"));
            Assert.IsFalse(CoCoSetupDependencyActions.IsSemanticVersionLower(
                "3.3.0", "3.2.2"));
            Assert.IsFalse(CoCoSetupDependencyActions.IsSemanticVersionLower(
                "file:../packages/newtonsoft", "3.2.2"));
        }

        [Test]
        public void ManifestValidationRejectsInvalidJson()
        {
            Assert.IsTrue(CoCoSetupDependencyActions.IsValidManifestJson("{\"value\":1}"));
            Assert.IsFalse(CoCoSetupDependencyActions.IsValidManifestJson("invalid"));
            Assert.IsFalse(CoCoSetupDependencyActions.IsValidManifestJson("[1,2]"));
        }

        /// <summary>
        /// 复刻 ConfigureProjectManifest 的 Newtonsoft 分支（纯内存对象层），
        /// 避免测试触碰真实 Packages/manifest.json。
        /// </summary>
        private static bool ApplyNewtonsoftRecommendation(
            JsonObject dependencies,
            ManifestDocument manifest,
            CoCoSetupDependencyActions.MessageCollector log)
        {
            if (!dependencies.TryGetString(
                    CoCoFlowUtility.NewtonsoftPackageName,
                    out var existing))
            {
                dependencies.Set(
                    CoCoFlowUtility.NewtonsoftPackageName,
                    new JsonString(CoCoFlowUtility.NewtonsoftMinimumVersion));
                manifest.Changed = true;
                log.Add("Added Newtonsoft dependency.", "已添加 Newtonsoft 依赖。");
                return true;
            }

            if (CoCoSetupDependencyActions.IsSemanticVersionLower(
                    existing,
                    CoCoFlowUtility.NewtonsoftMinimumVersion))
            {
                dependencies.Set(
                    CoCoFlowUtility.NewtonsoftPackageName,
                    new JsonString(CoCoFlowUtility.NewtonsoftMinimumVersion));
                manifest.Changed = true;
                log.Add("Updated Newtonsoft.", "已升级 Newtonsoft。");
                return true;
            }

            log.Add("Newtonsoft already satisfies.", "Newtonsoft 已满足。");
            return false;
        }
    }
}
