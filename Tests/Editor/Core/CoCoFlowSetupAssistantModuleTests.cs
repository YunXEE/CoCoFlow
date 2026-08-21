using System;
using System.Reflection;
using NUnit.Framework;

namespace CoCoFlow.Editor.Core.Tests
{
    public sealed class CoCoFlowSetupAssistantModuleTests
    {
        [Test]
        public void PoolingModuleRequiresUniTaskAndContentWithoutAddressables()
        {
            ModuleView module = FindModule("Pooling");

            Assert.That(module.RequiredSupportDefines, Is.EqualTo(new[]
            {
                "COCOFLOW_UNITASK_SUPPORT"
            }));
            Assert.That(module.RequiredAssemblies, Does.Contain("UniTask"));
            Assert.That(module.RequiredAssemblies, Does.Contain("CoCoFlow.Runtime.Content"));
            Assert.That(module.RequiredAssemblies, Does.Contain("CoCoFlow.Runtime.Pooling"));
            Assert.That(module.RequiredAssemblies, Does.Not.Contain("Unity.Addressables"));
            Assert.That(
                module.Description,
                Does.Contain("private pool implementation"));
        }

        [Test]
        public void TemporalPoolingModuleIsHostScopedAndHasNoAddressablesRequirement()
        {
            ModuleView module = FindModule("Pooling (Temporal)");

            Assert.That(module.RequiredSupportDefines, Is.EqualTo(new[]
            {
                "COCOFLOW_UNITASK_SUPPORT"
            }));
            Assert.That(module.RequiredAssemblies, Does.Contain("UniTask"));
            Assert.That(module.RequiredAssemblies, Does.Contain("CoCoFlow.Runtime.Pooling"));
            Assert.That(
                module.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.Pooling.Temporal"));
            Assert.That(
                module.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.StateGraphHost"));
            Assert.That(module.RequiredAssemblies, Does.Not.Contain("Unity.Addressables"));
            Assert.That(module.Description, Does.Contain("not world rollback"));
        }

        [Test]
        public void LocalizationCoreIsDefaultAndUiExtensionsRequireUiV2Support()
        {
            ModuleView localization = FindModule("Localization");
            Assert.That(localization.RequiredSupportDefines, Is.Empty);
            Assert.That(
                localization.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.Modules.Localization"));
            Assert.That(
                localization.RequiredAssemblies,
                Does.Not.Contain("CoCoFlow.Runtime.Modules.Localization.UI"));

            string[] uiDefines =
            {
                "COCOFLOW_UNITASK_SUPPORT",
                "COCOFLOW_DOTWEEN_SUPPORT",
                "UNITASK_DOTWEEN_SUPPORT"
            };
            ModuleView localizationUi = FindModule("Localization (UI)");
            Assert.That(
                localizationUi.RequiredSupportDefines,
                Is.EqualTo(uiDefines));
            Assert.That(
                localizationUi.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.Modules.Localization.UI"));

            ModuleView inputPromptUi = FindModule("Input Prompt (UI)");
            Assert.That(
                inputPromptUi.RequiredSupportDefines,
                Is.EqualTo(uiDefines));
            Assert.That(
                inputPromptUi.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.Modules.Input.UI"));
            Assert.That(
                inputPromptUi.RequiredAssemblies,
                Does.Contain("CoCoFlow.Runtime.Modules.Localization.UI"));
        }

        [TestCase(false, false, false, false, "")]
        [TestCase(false, true, false, false, "COCOFLOW_DOTWEEN_SUPPORT")]
        [TestCase(false, true, true, false, "COCOFLOW_DOTWEEN_SUPPORT")]
        [TestCase(true, false, false, false, "COCOFLOW_UNITASK_SUPPORT")]
        [TestCase(true, false, true, true, "COCOFLOW_UNITASK_SUPPORT")]
        [TestCase(
            true,
            true,
            true,
            false,
            "COCOFLOW_UNITASK_SUPPORT;COCOFLOW_DOTWEEN_SUPPORT")]
        [TestCase(
            true,
            true,
            true,
            true,
            "COCOFLOW_UNITASK_SUPPORT;COCOFLOW_DOTWEEN_SUPPORT;UNITASK_DOTWEEN_SUPPORT")]
        public void SupportDefinesRequireTheirExactOptionalDependencies(
            bool uniTaskAvailable,
            bool dotweenAvailable,
            bool dotweenModulesAvailable,
            bool uniTaskDotweenAvailable,
            string expectedDefines)
        {
            string[] actual = CoCoFlowSetupAssistant.SelectAvailableSupportDefines(
                uniTaskAvailable,
                dotweenAvailable,
                dotweenModulesAvailable,
                uniTaskDotweenAvailable);

            string[] expected = string.IsNullOrEmpty(expectedDefines)
                ? Array.Empty<string>()
                : expectedDefines.Split(';');
            Assert.That(actual, Is.EqualTo(expected));
        }


        [TestCase(false, false, CoCoUniTaskInstallForm.None)]
        [TestCase(true, false, CoCoUniTaskInstallForm.UpmRegistered)]
        [TestCase(true, true, CoCoUniTaskInstallForm.UpmRegistered)]
        [TestCase(false, true, CoCoUniTaskInstallForm.AssemblyOnly)]
        public void UniTaskFormPrefersUpmRegistrationOverAssemblyPresence(
            bool manifestHasUniTaskDependency,
            bool uniTaskAssemblyAvailable,
            CoCoUniTaskInstallForm expectedForm)
        {
            Assert.That(
                CoCoFlowSetupAssistant.ClassifyUniTaskForm(manifestHasUniTaskDependency, uniTaskAssemblyAvailable),
                Is.EqualTo(expectedForm));
        }

        [TestCase("2.5.11", CoCoUniTaskVersionCompatibility.Supported)]
        [TestCase("2.6.0", CoCoUniTaskVersionCompatibility.Supported)]
        [TestCase("2.5.10", CoCoUniTaskVersionCompatibility.BelowMinimum)]
        [TestCase("3.0.0", CoCoUniTaskVersionCompatibility.AtOrAboveMaximum)]
        [TestCase("4.1.2", CoCoUniTaskVersionCompatibility.AtOrAboveMaximum)]
        [TestCase(
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11",
            CoCoUniTaskVersionCompatibility.Supported)]
        [TestCase(
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#3.0.0",
            CoCoUniTaskVersionCompatibility.AtOrAboveMaximum)]
        [TestCase("file:../UniTask", CoCoUniTaskVersionCompatibility.Unknown)]
        [TestCase("", CoCoUniTaskVersionCompatibility.Unknown)]
        [TestCase(null, CoCoUniTaskVersionCompatibility.Unknown)]
        [TestCase("not-a-version", CoCoUniTaskVersionCompatibility.Unknown)]
        public void CoCoUniTaskVersionPolicyEvaluatesSemverAndGitUrlSuffix(
            string dependency,
            CoCoUniTaskVersionCompatibility expected)
        {
            Assert.That(
                CoCoUniTaskVersionPolicy.Evaluate(dependency),
                Is.EqualTo(expected));
        }

        private static ModuleView FindModule(string displayName)
        {
            FieldInfo modulesField = typeof(CoCoFlowSetupAssistant).GetField(
                "Modules",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(modulesField, Is.Not.Null);

            Array modules = modulesField.GetValue(null) as Array;
            Assert.That(modules, Is.Not.Null);

            foreach (object module in modules)
            {
                Type moduleType = module.GetType();
                string candidateName = ReadProperty<string>(
                    moduleType,
                    module,
                    "DisplayName");
                if (candidateName != displayName)
                {
                    continue;
                }

                return new ModuleView(
                    candidateName,
                    ReadProperty<string[]>(
                        moduleType,
                        module,
                        "RequiredSupportDefines"),
                    ReadProperty<string[]>(
                        moduleType,
                        module,
                        "RequiredAssemblies"),
                    ReadProperty<string>(
                        moduleType,
                        module,
                        "Description"));
            }

            Assert.Fail("Setup Assistant module not found: " + displayName);
            return default;
        }

        private static T ReadProperty<T>(
            Type type,
            object instance,
            string propertyName)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null);
            return (T)property.GetValue(instance);
        }

        private readonly struct ModuleView
        {
            public ModuleView(
                string displayName,
                string[] requiredSupportDefines,
                string[] requiredAssemblies,
                string description)
            {
                DisplayName = displayName;
                RequiredSupportDefines = requiredSupportDefines;
                RequiredAssemblies = requiredAssemblies;
                Description = description;
            }

            public string DisplayName { get; }

            public string[] RequiredSupportDefines { get; }

            public string[] RequiredAssemblies { get; }

            public string Description { get; }
        }
    }
}
