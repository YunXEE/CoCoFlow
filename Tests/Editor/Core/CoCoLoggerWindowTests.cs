using System;
using System.Linq;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Core.Tests
{
    /// <summary>
    /// CoCoLog 试点迁移的 focused 测试（方案 §2.5：只写不跑，W01 集成执行）。
    /// 覆盖功能等价 invariant #1-#7（数据层）+ badge 映射 + Common 组件构造契约。
    /// </summary>
    public class CoCoLoggerWindowTests
    {
        private static CoCoLogEvent MakeEvent(
            string module = "Core",
            string message = "m",
            CoCoLogLevel level = CoCoLogLevel.Log)
        {
            return new CoCoLogEvent
            {
                Level = level,
                ModuleName = module,
                ClassName = "TestClass",
                Message = message,
                Timestamp = new DateTime(2026, 9, 1, 12, 0, 0)
            };
        }

        // ── invariant #1：未知模块首现默认启用并注册过滤项 ──

        [Test]
        public void UnknownModule_IsRegisteredAndVisibleByDefault()
        {
            var data = new CoCoLoggerWindowData();
            Assert.AreEqual(0, data.ModuleCount, "初始无模块");
            data.Add(MakeEvent(module: "BrandNewModule"));

            Assert.IsTrue(data.IsModuleVisible("BrandNewModule"));
            CollectionAssert.Contains(data.ModuleNames.ToList(), "BrandNewModule");
            Assert.AreEqual(1, data.VisibleEvents.Count, "默认启用的未知模块应出现在投影中");
            Assert.AreEqual(1, data.ModuleCount, "BUG-043：模块集合变化可检测（ModuleCount 变化）");
        }

        [Test]
        public void PreloadKnownModules_RegistersAllKnownModulesEnabled()
        {
            var data = new CoCoLoggerWindowData();
            data.PreloadKnownModules();

            foreach (string module in data.ModuleNames)
            {
                Assert.IsTrue(data.IsModuleVisible(module), $"{module} 应默认启用");
            }
        }

        // ── invariant #2：到达顺序 = 投影顺序 ──

        [Test]
        public void Events_PreserveArrivalOrder()
        {
            var data = new CoCoLoggerWindowData();
            data.Add(MakeEvent(message: "first"));
            data.Add(MakeEvent(message: "second"));
            data.Add(MakeEvent(message: "third"));

            Assert.AreEqual(
                new[] { "first", "second", "third" },
                data.VisibleEvents.Select(e => e.Message).ToArray());
        }

        // ── invariant #3：四字段投影完整 ──

        [Test]
        public void Events_ProjectAllFields()
        {
            var data = new CoCoLoggerWindowData();
            var source = MakeEvent(module: "Map", message: "hello");
            source.Timestamp = new DateTime(2026, 9, 1, 8, 30, 5);
            data.Add(source);

            CoCoLogEvent projected = data.VisibleEvents.Single();
            Assert.AreEqual(CoCoLogLevel.Log, projected.Level);
            Assert.AreEqual("Map", projected.ModuleName);
            Assert.AreEqual("TestClass", projected.ClassName);
            Assert.AreEqual("hello", projected.Message);
            Assert.AreEqual(new DateTime(2026, 9, 1, 8, 30, 5), projected.Timestamp);
        }

        // ── invariant #4：过滤只改可见投影，不删数据 ──

        [Test]
        public void Filter_ChangesProjectionOnly_NotUnderlyingData()
        {
            var data = new CoCoLoggerWindowData();
            data.Add(MakeEvent(module: "Core"));
            data.Add(MakeEvent(module: "Map"));

            data.SetModuleVisible("Map", false);

            Assert.AreEqual(1, data.VisibleEvents.Count, "被过滤模块应从投影消失");
            Assert.AreEqual("Core", data.VisibleEvents.Single().ModuleName);
            Assert.AreEqual(2, data.TotalCount, "过滤不得删除数据（invariant #4）");

            data.SetModuleVisible("Map", true);
            Assert.AreEqual(2, data.VisibleEvents.Count, "恢复过滤后投影完整回归");
        }

        // ── invariant #5：计数口径 = 到达总数（不受过滤影响） ──

        [Test]
        public void TotalCount_IsUnaffectedByFiltering()
        {
            var data = new CoCoLoggerWindowData();
            data.Add(MakeEvent(module: "Core"));
            data.Add(MakeEvent(module: "Core"));
            data.Add(MakeEvent(module: "Map"));

            data.SetModuleVisible("Core", false);

            Assert.AreEqual(3, data.TotalCount, "总数口径不受过滤影响（invariant #5）");
            Assert.AreEqual(1, data.VisibleEvents.Count);
        }

        // ── invariant #6：Clear 数据清空 + 计数归零（过滤表保留） ──

        [Test]
        public void Clear_EmptiesData_KeepsFilters()
        {
            var data = new CoCoLoggerWindowData();
            data.PreloadKnownModules();
            data.Add(MakeEvent(module: "ExtraModule"));

            data.Clear();

            Assert.AreEqual(0, data.TotalCount);
            Assert.AreEqual(0, data.VisibleEvents.Count);
            CollectionAssert.Contains(data.ModuleNames.ToList(), "ExtraModule", "过滤表应保留");
        }

        // ── invariant #7：MaxLogs 环形截断（移除最旧） ──

        [Test]
        public void Overflow_RemovesOldest_AndCapsAtMaxLogs()
        {
            var data = new CoCoLoggerWindowData();

            for (int i = 0; i < CoCoLoggerWindowData.MaxLogs + 5; i++)
            {
                data.Add(MakeEvent(message: $"m{i}"));
            }

            Assert.AreEqual(CoCoLoggerWindowData.MaxLogs, data.TotalCount);
            Assert.AreEqual(
                "m5",
                data.VisibleEvents.First().Message,
                "最旧的 5 条应被移除（环形截断）");
        }

        // ── invariant #9 的映射面：Level → badge 语义 ──

        [Test]
        public void LevelToBadgeKind_MapsAllLevels()
        {
            Assert.AreEqual(CoCoEditorBadgeKind.Neutral, CoCoLoggerWindow.LevelToBadgeKind(CoCoLogLevel.Log));
            Assert.AreEqual(CoCoEditorBadgeKind.Warning, CoCoLoggerWindow.LevelToBadgeKind(CoCoLogLevel.Warning));
            Assert.AreEqual(CoCoEditorBadgeKind.Error, CoCoLoggerWindow.LevelToBadgeKind(CoCoLogLevel.Error));
        }

        // ── Common 组件构造契约（D3/D12 冻结表面） ──

        [Test]
        public void CreateBadge_CarriesKindClassAndText()
        {
            VisualElement badge = CoCoEditorElements.CreateBadge("W", CoCoEditorBadgeKind.Warning);

            StringAssert.Contains("ccflow-badge", badge.GetClasses().First());
            Assert.IsTrue(badge.ClassListContains("ccflow-badge--warning"));

            var text = badge.Q<Label>("ccflow-badge-text");
            Assert.IsNotNull(text);
            Assert.AreEqual("W", text.text);

            CoCoEditorElements.SetBadgeKind(badge, CoCoEditorBadgeKind.Error);
            Assert.IsTrue(badge.ClassListContains("ccflow-badge--error"));
            Assert.IsFalse(badge.ClassListContains("ccflow-badge--warning"));
        }

        [Test]
        public void CreateDiagnosticRow_IncludesBadgeMessageAndOptionalLocate()
        {
            bool located = false;
            VisualElement row = CoCoEditorElements.CreateDiagnosticRow(
                "compilation blocked", CoCoEditorBadgeKind.Error, () => located = true);

            Assert.IsTrue(row.ClassListContains("ccflow-diagnostic-row"));
            Assert.IsNotNull(row.Q<Label>("ccflow-diagnostic-message"));

            // BUG-044：徽章携带可读严重度文本（D3 不单靠颜色）
            var badgeText = row.Q<VisualElement>("level-badge")?.Q<Label>("ccflow-badge-text");
            if (badgeText == null)
            {
                badgeText = row.Q(className: "ccflow-badge")?.Q<Label>("ccflow-badge-text");
            }

            Assert.IsNotNull(badgeText, "诊断徽章应含文本 Label");
            StringAssert.IsMatch("Error|错误", badgeText.text, "严重度文本非空且随语言");

            var locate = row.Q<Button>();
            Assert.IsNotNull(locate, "提供 locate 时应有按钮");
            StringAssert.IsMatch("Locate|定位", locate.text, "BUG-044：Locate 文案走双语层");

            VisualElement bare = CoCoEditorElements.CreateDiagnosticRow("info", CoCoEditorBadgeKind.Info);
            Assert.IsNull(bare.Q<Button>(), "未提供 locate 时不应有按钮");
            Assert.IsFalse(located);
        }

        [Test]
        public void CreateEmptyState_BuildsThreePartCopy()
        {
            VisualElement empty = CoCoEditorElements.CreateEmptyState("t", "m", "step", "alt");

            Assert.IsTrue(empty.ClassListContains("ccflow-empty"));
            Assert.IsNotNull(empty.Q<Label>("ccflow-empty-title"));
            Assert.IsNotNull(empty.Q<Label>("ccflow-empty-message"));
            Assert.IsNotNull(empty.Q<Label>("ccflow-empty-first-step"));
            Assert.IsNotNull(empty.Q<Label>("ccflow-empty-alternative"));
        }

        // ── 双语层（D10） ──

        [Test]
        public void Text_ReturnsLanguageSide()
        {
            CoCoEditorLanguage original = CoCoEditorLocalization.CurrentLanguage;
            try
            {
                CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.English);
                Assert.AreEqual("Clear", CoCoEditorLocalization.Text("Clear", "清空"));

                CoCoEditorLocalization.SetLanguage(CoCoEditorLanguage.SimplifiedChinese);
                Assert.AreEqual("清空", CoCoEditorLocalization.Text("Clear", "清空"));
            }
            finally
            {
                CoCoEditorLocalization.SetLanguage(original);
            }
        }

        [Test]
        public void SetLanguage_RaisesLanguageChanged()
        {
            CoCoEditorLanguage original = CoCoEditorLocalization.CurrentLanguage;
            int raised = 0;
            CoCoEditorLocalization.LanguageChanged += OnRaised;
            try
            {
                CoCoEditorLocalization.SetLanguage(
                    original == CoCoEditorLanguage.English
                        ? CoCoEditorLanguage.SimplifiedChinese
                        : CoCoEditorLanguage.English);
                Assert.AreEqual(1, raised, "SetLanguage 应触发一次 LanguageChanged");
            }
            finally
            {
                CoCoEditorLocalization.LanguageChanged -= OnRaised;
                CoCoEditorLocalization.SetLanguage(original);
            }

            void OnRaised() => raised++;
        }
    }
}
