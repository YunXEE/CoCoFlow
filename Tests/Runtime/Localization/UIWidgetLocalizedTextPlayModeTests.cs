using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Modules.Localization;
using CoCoFlow.Runtime.Modules.Localization.UI;
using CoCoFlow.Runtime.Modules.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Localization
{
    public sealed class UIWidgetLocalizedTextPlayModeTests
    {
        [UnityTest]
        public IEnumerator MissingTextTargetProducesReadableDiagnostic()
        {
            var gameObject = new GameObject("LocalizedWidgetTest");
            gameObject.SetActive(false);
            var widget = gameObject.AddComponent<UIWidgetLocalizedText>();

            widget.ResetState();
            yield return null;

            Assert.AreEqual(
                LocalizationDiagnosticCode.MissingTextTarget,
                widget.LastDiagnostic.Code);
            Assert.IsNotEmpty(widget.LastDiagnostic.Message);

            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator FailedAsyncLoadWithoutTextTargetRemainsDiagnosable()
        {
            FieldInfo instanceField = typeof(LocalizationSettings).GetField(
                "s_Instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField);
            var previousSettings =
                instanceField.GetValue(null) as LocalizationSettings;
            var settings =
                ScriptableObject.CreateInstance<TestLocalizationSettings>();
            Locale english = Locale.CreateLocale("en");
            var tableProvider = new TestStringTableProvider();
            var database = new TestLocalizedStringDatabase
            {
                TableProvider = tableProvider
            };
            settings.SetAvailableLocales(new TestLocalesProvider(english));
            settings.SetStringDatabase(database);
            LocalizationSettings.Instance = settings;
            settings.SetSelectedLocale(english);

            var panelObject = new GameObject("FailedLocalizedPanel");
            panelObject.AddComponent<TestPanel>();
            var widgetObject = new GameObject(
                "FailedLocalizedWidget",
                typeof(RectTransform),
                typeof(CanvasGroup));
            widgetObject.transform.SetParent(panelObject.transform, false);
            widgetObject.SetActive(false);
            var widget = widgetObject.AddComponent<UIWidgetLocalizedText>();
            SetField(
                widget,
                "localizedString",
                new LocalizedString("Pre14 Failure", "Missing"));
            widgetObject.SetActive(true);
            widget.SetArguments(new { binding = "Space" });

            yield return null;
            yield return null;

            Assert.AreEqual(
                LocalizationDiagnosticCode.MissingTextTarget,
                widget.LastDiagnostic.Code);
            Assert.IsNotEmpty(widget.LastDiagnostic.Message);

            widgetObject.SetActive(false);
            Object.Destroy(widgetObject);
            Object.Destroy(panelObject);
            LocalizationSettings.Instance = previousSettings;
            ((System.IDisposable)settings).Dispose();
            settings.DisposeTestResources();
            database.DisposeTestResources();
            tableProvider.Dispose();
            Object.Destroy(settings);
            Object.Destroy(english);
            yield return null;
        }

        [Test]
        public void SmartStringArgumentsCanBeReplacedWithoutReopeningScreen()
        {
            var gameObject = new GameObject("LocalizedArgumentsTest");
            gameObject.SetActive(false);
            var widget = gameObject.AddComponent<UIWidgetLocalizedText>();

            Assert.DoesNotThrow(() =>
                widget.SetArguments(new { binding = "Space" }));
            Assert.DoesNotThrow(() =>
                widget.SetArguments(new { binding = "Gamepad South" }));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void DefaultDiagnosticMatchesConstructedNone()
        {
            LocalizationDiagnostic defaultValue = default;
            var constructed = new LocalizationDiagnostic(
                LocalizationDiagnosticCode.None,
                null);

            Assert.AreEqual(string.Empty, defaultValue.Message);
            Assert.AreEqual(string.Empty, LocalizationDiagnostic.None.Message);
            Assert.AreEqual(constructed, defaultValue);
            Assert.AreEqual(constructed.GetHashCode(), defaultValue.GetHashCode());
        }

        [UnityTest]
        public IEnumerator EmptyLocalizedStringSetArgumentsRestoresFallback()
        {
            var panelObject = new GameObject("EmptyLocalizedPanel");
            panelObject.AddComponent<TestPanel>();
            var widgetObject = new GameObject(
                "EmptyLocalizedWidget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup));
            widgetObject.transform.SetParent(panelObject.transform, false);
            widgetObject.SetActive(false);
            var target = widgetObject.AddComponent<TextMeshProUGUI>();
            var widget = widgetObject.AddComponent<UIWidgetLocalizedText>();
            SetField(widget, "targetText", target);
            SetField(widget, "localizedString", new LocalizedString());

            widget.ClearPresentation();
            widget.SetFallback("Fallback");
            widget.SetArguments(new { binding = "Space" });

            Assert.AreEqual("Fallback", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.InvalidTableOrEntry,
                widget.LastDiagnostic.Code);

            widgetObject.SetActive(false);
            Object.Destroy(widgetObject);
            Object.Destroy(panelObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LocaleLoadFailureFallsBackAndLaterLocaleRecovers()
        {
            FieldInfo instanceField = typeof(LocalizationSettings).GetField(
                "s_Instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField);
            var previousSettings =
                instanceField.GetValue(null) as LocalizationSettings;
            var settings =
                ScriptableObject.CreateInstance<TestLocalizationSettings>();
            Locale english = Locale.CreateLocale("en");
            Locale japanese = Locale.CreateLocale("ja");
            var tableProvider = new TestStringTableProvider();
            var database = new TestLocalizedStringDatabase
            {
                TableProvider = tableProvider
            };
            settings.SetAvailableLocales(
                new TestLocalesProvider(english, japanese));
            settings.SetStringDatabase(database);
            LocalizationSettings.Instance = settings;
            settings.SetSelectedLocale(english);

            var panelObject = new GameObject("LocaleFailurePanel");
            panelObject.AddComponent<TestPanel>();
            var widgetObject = new GameObject(
                "LocaleFailureWidget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup));
            widgetObject.transform.SetParent(panelObject.transform, false);
            widgetObject.SetActive(false);
            var target = widgetObject.AddComponent<TextMeshProUGUI>();
            var widget = widgetObject.AddComponent<UIWidgetLocalizedText>();
            SetField(widget, "targetText", target);
            SetField(
                widget,
                "localizedString",
                new LocalizedString("Pre14 Prompts", "Press"));
            SetField(widget, "fallbackText", "Fallback");
            widget.SetArguments(new { binding = "Space" });
            widgetObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.AreEqual("Press Space", target.text);

            settings.SetSelectedLocale(japanese);
            yield return null;
            yield return null;

            Assert.AreEqual("Fallback", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.LoadFailed,
                widget.LastDiagnostic.Code);

            settings.SetSelectedLocale(english);
            yield return null;
            yield return null;
            Assert.AreEqual("Press Space", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.None,
                widget.LastDiagnostic.Code);

            widgetObject.SetActive(false);
            Object.Destroy(widgetObject);
            Object.Destroy(panelObject);
            LocalizationSettings.Instance = previousSettings;
            ((System.IDisposable)settings).Dispose();
            settings.DisposeTestResources();
            database.DisposeTestResources();
            tableProvider.Dispose();
            Object.Destroy(settings);
            Object.Destroy(english);
            Object.Destroy(japanese);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LocaleArgumentsAndEnableLifecycleRefreshCurrentScreen()
        {
            FieldInfo instanceField = typeof(LocalizationSettings).GetField(
                "s_Instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField);
            var previousSettings =
                instanceField.GetValue(null) as LocalizationSettings;
            var settings =
                ScriptableObject.CreateInstance<TestLocalizationSettings>();
            Locale english = Locale.CreateLocale("en");
            Locale french = Locale.CreateLocale("fr");
            var locales = new TestLocalesProvider(english, french);
            var tableProvider = new TestStringTableProvider();
            var database = new TestLocalizedStringDatabase
            {
                TableProvider = tableProvider
            };
            settings.SetAvailableLocales(locales);
            settings.SetStringDatabase(database);
            LocalizationSettings.Instance = settings;
            settings.SetSelectedLocale(english);

            var panelObject = new GameObject("LocalizedPanel");
            panelObject.AddComponent<TestPanel>();
            var widgetObject = new GameObject(
                "LocalizedWidget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup));
            widgetObject.transform.SetParent(panelObject.transform, false);
            widgetObject.SetActive(false);
            var target = widgetObject.AddComponent<TextMeshProUGUI>();
            var widget = widgetObject.AddComponent<UIWidgetLocalizedText>();
            var localizedString = new LocalizedString(
                "Pre14 Prompts",
                "Press");
            SetField(widget, "targetText", target);
            SetField(widget, "localizedString", localizedString);
            SetField(widget, "fallbackText", "Fallback");
            widget.SetArguments(new { binding = "Space" });

            widgetObject.SetActive(true);
            yield return null;
            yield return null;

            Assert.IsTrue(localizedString.HasChangeHandler);
            Assert.AreEqual("Press Space", target.text);

            widget.SetArguments(new { binding = "Enter" });
            yield return null;
            Assert.AreEqual("Press Enter", target.text);

            settings.SetSelectedLocale(french);
            yield return null;
            yield return null;
            Assert.AreEqual("Appuyez sur Enter", target.text);

            widget.ClearPresentation();
            Assert.AreEqual(string.Empty, target.text);
            Assert.IsFalse(localizedString.HasChangeHandler);
            Assert.IsEmpty(localizedString.Arguments);
            settings.SetSelectedLocale(english);
            yield return null;
            yield return null;
            Assert.AreEqual(string.Empty, target.text);
            widget.ResetState();
            Assert.AreEqual(string.Empty, target.text);

            widget.SetFallback("Space");
            widget.SetArguments(new { binding = "Space" });
            yield return null;
            Assert.AreEqual("Press Space", target.text);

            widgetObject.SetActive(false);
            Assert.IsFalse(localizedString.HasChangeHandler);

            widgetObject.SetActive(true);
            yield return null;
            Assert.IsTrue(localizedString.HasChangeHandler);
            Assert.AreEqual("Press Space", target.text);

            widgetObject.SetActive(false);
            Object.Destroy(widgetObject);
            Object.Destroy(panelObject);
            LocalizationSettings.Instance = previousSettings;
            ((System.IDisposable)settings).Dispose();
            settings.DisposeTestResources();
            database.DisposeTestResources();
            tableProvider.Dispose();
            Object.Destroy(settings);
            Object.Destroy(english);
            Object.Destroy(french);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NullLocalizedStringRestoresFallbackAfterClearPresentation()
        {
            var panelObject = new GameObject("FallbackLocalizedPanel");
            panelObject.AddComponent<TestPanel>();
            var widgetObject = new GameObject(
                "FallbackLocalizedWidget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup));
            widgetObject.transform.SetParent(panelObject.transform, false);
            widgetObject.SetActive(false);
            var target = widgetObject.AddComponent<TextMeshProUGUI>();
            var widget = widgetObject.AddComponent<UIWidgetLocalizedText>();
            SetField(widget, "targetText", target);
            SetField(widget, "localizedString", null);
            widgetObject.SetActive(true);
            yield return null;

            widget.ClearPresentation();
            widget.SetFallback("Press Space");
            widget.SetArguments(new { binding = "Space" });

            Assert.AreEqual("Press Space", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.MissingLocalizedString,
                widget.LastDiagnostic.Code);

            widget.ResetState();
            Assert.AreEqual("Press Space", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.MissingLocalizedString,
                widget.LastDiagnostic.Code);

            widgetObject.SetActive(false);
            widgetObject.SetActive(true);
            yield return null;
            Assert.AreEqual("Press Space", target.text);
            Assert.AreEqual(
                LocalizationDiagnosticCode.MissingLocalizedString,
                widget.LastDiagnostic.Code);

            widgetObject.SetActive(false);
            Object.Destroy(widgetObject);
            Object.Destroy(panelObject);
            yield return null;
        }

        private static void SetField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private sealed class TestPanel : UIPanelBase
        {
        }

        private sealed class TestLocalizationSettings : LocalizationSettings
        {
            private readonly ResourceManager _resourceManager =
                new ResourceManager();
            private AsyncOperationHandle<LocalizationSettings>
                _initializationOperation;

            public override AsyncOperationHandle<LocalizationSettings>
                GetInitializationOperation()
            {
                if (!_initializationOperation.IsValid())
                {
                    _initializationOperation =
                        _resourceManager.CreateCompletedOperation<LocalizationSettings>(
                            this,
                            null);
                }

                return _initializationOperation;
            }

            internal void DisposeTestResources()
            {
                if (_initializationOperation.IsValid())
                {
                    _resourceManager.Release(_initializationOperation);
                    _initializationOperation = default;
                }

                _resourceManager.Dispose();
            }
        }

        private sealed class TestLocalizedStringDatabase :
            LocalizedStringDatabase
        {
            private readonly ResourceManager _resourceManager =
                new ResourceManager();

            public override AsyncOperationHandle<TableEntryResult>
                GetTableEntryAsync(
                    TableReference tableReference,
                    TableEntryReference tableEntryReference,
                    Locale locale = null,
                    FallbackBehavior fallbackBehavior =
                        FallbackBehavior.UseProjectSettings)
            {
                Locale requestedLocale =
                    locale ?? LocalizationSettings.SelectedLocale;
                string tableName = tableReference.TableCollectionName;
                if (tableName == "Pre14 Failure")
                {
                    return CreateFailure(
                        "Expected Pre14 localization load failure.");
                }

                if (tableName == "Pre14 Prompts" &&
                    requestedLocale != null &&
                    requestedLocale.Identifier.Code == "ja")
                {
                    return CreateFailure(
                        "Expected locale switch failure.");
                }

                return base.GetTableEntryAsync(
                    tableReference,
                    tableEntryReference,
                    locale,
                    fallbackBehavior);
            }

            internal void DisposeTestResources()
            {
                _resourceManager.Dispose();
            }

            private AsyncOperationHandle<TableEntryResult> CreateFailure(
                string message)
            {
                return _resourceManager.CreateCompletedOperation(
                    default(TableEntryResult),
                    message);
            }
        }

        private sealed class TestLocalesProvider : ILocalesProvider
        {
            public TestLocalesProvider(params Locale[] locales)
            {
                Locales = new List<Locale>(locales);
            }

            public List<Locale> Locales { get; }

            public Locale GetLocale(LocaleIdentifier id) =>
                Locales.Find(locale =>
                    locale != null && locale.Identifier == id);

            public void AddLocale(Locale locale)
            {
                if (locale != null && !Locales.Contains(locale))
                {
                    Locales.Add(locale);
                }
            }

            public bool RemoveLocale(Locale locale) =>
                Locales.Remove(locale);
        }

        private sealed class TestStringTableProvider :
            ITableProvider,
            System.IDisposable
        {
            private readonly ResourceManager _resourceManager =
                new ResourceManager();
            private readonly List<Object> _tables = new List<Object>();

            public AsyncOperationHandle<TTable> ProvideTableAsync<TTable>(
                string tableCollectionName,
                Locale locale)
                where TTable : LocalizationTable
            {
                if (typeof(TTable) != typeof(StringTable) ||
                    locale == null ||
                    tableCollectionName != "Pre14 Prompts")
                {
                    return default;
                }

                var table = ScriptableObject.CreateInstance<StringTable>();
                var shared =
                    ScriptableObject.CreateInstance<SharedTableData>();
                shared.TableCollectionName = tableCollectionName;
                table.SharedData = shared;
                table.LocaleIdentifier = locale.Identifier;
                StringTableEntry entry = table.AddEntry(
                    "Press",
                    locale.Identifier.Code == "fr"
                        ? "Appuyez sur {binding}"
                        : "Press {binding}");
                entry.IsSmart = true;
                _tables.Add(table);
                _tables.Add(shared);
                return _resourceManager.CreateCompletedOperation(
                    table as TTable,
                    null);
            }

            public void Dispose()
            {
                _resourceManager.Dispose();
                foreach (Object table in _tables)
                {
                    if (table != null)
                    {
                        Object.Destroy(table);
                    }
                }

                _tables.Clear();
            }
        }
    }
}
