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
        private enum Command : byte
        {
            Submit = 1
        }

        [Test]
        public void QueueAndBatchCapacitiesAreFrozenWithoutTypeNameSuffixes()
        {
            var queue = new InputCommandQueue<Command>();

            Assert.AreEqual(32, queue.Capacity);
            Assert.AreEqual(8, InputCommandBatch<Command>.Capacity);
            Assert.IsFalse(
                RuntimeHelpers.IsReferenceOrContainsReferences<
                    InputCommandBatch<Command>>());
        }

        [Test]
        public void WarmQueueAndBatchHotPathAllocatesNoManagedMemory()
        {
            var queue = new InputCommandQueue<Command>();
            InputCommandBatch<Command> batch = default;
            queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Performed,
                1UL);
            queue.DrainTo(ref batch);
            _ = System.GC.GetAllocatedBytesForCurrentThread();

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            int drained = 0;
            for (ulong sequence = 2; sequence < 1002; sequence++)
            {
                batch = default;
                succeeded &= queue.TryEnqueue(
                    Command.Submit,
                    InputCommandPhase.Performed,
                    sequence);
                drained += queue.DrainTo(ref batch);
            }

            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.IsTrue(succeeded);
            Assert.AreEqual(1000, drained);
            Assert.AreEqual(before, after);
        }

        [Test]
        public void QueuePreservesOrderAndDefersCommandsBeyondBatchCapacity()
        {
            var queue = new InputCommandQueue<Command>(16);
            for (ulong sequence = 1; sequence <= 10; sequence++)
            {
                Assert.IsTrue(queue.TryEnqueue(
                    Command.Submit,
                    sequence % 2 == 0
                        ? InputCommandPhase.Canceled
                        : InputCommandPhase.Performed,
                    sequence));
            }

            InputCommandBatch<Command> first = default;
            Assert.AreEqual(8, queue.DrainTo(ref first));
            Assert.AreEqual(InputCommandBatch<Command>.Capacity, first.Count);
            Assert.AreEqual(2, queue.Count);
            for (int index = 0; index < first.Count; index++)
            {
                Assert.IsTrue(first.TryGet(index, out InputCommand<Command> item));
                Assert.AreEqual((ulong)(index + 1), item.Sequence);
                Assert.AreEqual(
                    index % 2 == 0
                        ? InputCommandPhase.Performed
                        : InputCommandPhase.Canceled,
                    item.Phase);
            }

            InputCommandBatch<Command> second = default;
            Assert.AreEqual(2, queue.DrainTo(ref second));
            Assert.IsTrue(second.TryGet(0, out InputCommand<Command> ninth));
            Assert.IsTrue(second.TryGet(1, out InputCommand<Command> tenth));
            Assert.AreEqual(9UL, ninth.Sequence);
            Assert.AreEqual(10UL, tenth.Sequence);
        }

        [Test]
        public void FullQueueRejectsNewestAndCountsOverflow()
        {
            var queue = new InputCommandQueue<Command>(2);
            Assert.IsTrue(queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Performed,
                1UL));
            Assert.IsTrue(queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Canceled,
                2UL));
            Assert.IsFalse(queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Performed,
                3UL));
            Assert.AreEqual(1UL, queue.OverflowCount);

            InputCommandBatch<Command> batch = default;
            queue.DrainTo(ref batch);
            Assert.IsTrue(batch.TryGet(0, out InputCommand<Command> first));
            Assert.IsTrue(batch.TryGet(1, out InputCommand<Command> second));
            Assert.AreEqual(1UL, first.Sequence);
            Assert.AreEqual(2UL, second.Sequence);
        }

        [Test]
        public void ClearCreatesAnInputFenceWithoutResettingOverflowEvidence()
        {
            var queue = new InputCommandQueue<Command>(1);
            queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Performed,
                1UL);
            queue.TryEnqueue(
                Command.Submit,
                InputCommandPhase.Performed,
                2UL);

            queue.Clear();

            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(1UL, queue.OverflowCount);
        }

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
