using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Tests.Editor.Input
{
    public sealed class InputContractTests
    {
        [Test]
        public void GlyphCatalogUsesExactThenBaseLayoutAndSupportsTextFallback()
        {
            var catalog = ScriptableObject.CreateInstance<InputGlyphCatalog>();
            var texture = new Texture2D(2, 2);
            Sprite glyph = Sprite.Create(
                texture,
                new Rect(0f, 0f, 2f, 2f),
                Vector2.zero);
            var entry = new InputGlyphCatalog.Entry();
            SetField(entry, "deviceLayout", "Gamepad");
            SetField(entry, "controlPath", "buttonSouth");
            SetField(entry, "glyph", glyph);
            SetField(
                catalog,
                "entries",
                new List<InputGlyphCatalog.Entry> { entry });

            Assert.IsTrue(catalog.TryResolve(
                "Gamepad",
                "buttonSouth",
                out Sprite exact));
            Assert.AreSame(glyph, exact);

            const string derivedLayout = "Pre14DerivedGamepad";
            InputSystem.RegisterLayout(
                "{\"name\":\"" + derivedLayout +
                "\",\"extend\":\"Gamepad\"}");
            try
            {
                Assert.IsTrue(catalog.TryResolve(
                    derivedLayout,
                    "buttonSouth",
                    out Sprite inherited));
                Assert.AreSame(glyph, inherited);
                Assert.IsFalse(catalog.TryResolve(
                    derivedLayout,
                    "dpad/up",
                    out _));

                var fallback = new InputPromptSnapshot(
                    System.Guid.NewGuid(),
                    0,
                    "D-Pad Up",
                    derivedLayout,
                    "dpad/up",
                    null);
                Assert.AreEqual(
                    InputPromptFallbackState.BindingText,
                    fallback.FallbackState);
            }
            finally
            {
                InputSystem.RemoveLayout(derivedLayout);
                Object.DestroyImmediate(glyph);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(catalog);
            }
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
