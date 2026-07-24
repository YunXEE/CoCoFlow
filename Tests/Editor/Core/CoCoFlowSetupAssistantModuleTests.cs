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
