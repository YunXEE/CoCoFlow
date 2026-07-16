using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphAuthoringEnvelopeTests
    {
        private CoCoGraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
            catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
        }

        [Test]
        public void OneDimensionalCollectionsAreAccepted()
        {
            var config = new TestStateAuthoringConfig
            {
                Value = 1,
                Values = new[] { 1, 2, 3 },
                Items = new List<int> { 4, 5, 6 }
            };

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsFalse(HasAuthoringEnvelopeError(result));
        }

        [Test]
        public void AConfigSharedByTwoTopLevelSerializeReferenceFieldsIsAccepted()
        {
            var config = new TestStateAuthoringConfig { Value = 1 };

            CoCoStateGraphAssetCompileResult result = CompileSharedAcrossTwoStates(config);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsFalse(HasAuthoringEnvelopeError(result));
        }

        [Test]
        public void SharedInlineReferenceIsRejectedBeforeTheFreezerRuns()
        {
            var shared = new InlineLeaf { Value = 7 };
            var config = new InlineAliasConfig
            {
                First = shared,
                Second = shared
            };

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsTrue(HasAuthoringEnvelopeError(result));
        }

        [Test]
        public void PolymorphicInlineReferenceIsRejectedBeforeTheFreezerRuns()
        {
            var config = new PolymorphicInlineConfig
            {
                Value = new InlineDerived { Value = 7, ExtraValue = 9 }
            };

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsTrue(HasAuthoringEnvelopeError(result));
        }

        [Test]
        public void ObjectCycleIsRejectedBeforeTheFreezerRuns()
        {
            var node = new CyclicNode { Value = 1 };
            node.Next = node;
            var config = new CyclicConfig { Value = node };

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsTrue(HasAuthoringEnvelopeError(result));
        }

        [TestCase(NonSerializedStateKind.Delegate)]
        [TestCase(NonSerializedStateKind.Dictionary)]
        [TestCase(NonSerializedStateKind.UnityObject)]
        [TestCase(NonSerializedStateKind.PrivateField)]
        [TestCase(NonSerializedStateKind.ReadonlyField)]
        public void NonSerializedRuntimeStateIsRejectedBeforeTheFreezerRuns(
            NonSerializedStateKind kind)
        {
            CoCoStateConfig config = CreateNonSerializedConfig(kind);

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsTrue(HasAuthoringEnvelopeError(result));
        }

        [TestCase(ForbiddenSerializedValueKind.Delegate)]
        [TestCase(ForbiddenSerializedValueKind.Dictionary)]
        [TestCase(ForbiddenSerializedValueKind.UnityObject)]
        [TestCase(ForbiddenSerializedValueKind.MultidimensionalArray)]
        [TestCase(ForbiddenSerializedValueKind.JaggedArray)]
        [TestCase(ForbiddenSerializedValueKind.NestedList)]
        [TestCase(ForbiddenSerializedValueKind.SharedSerializeReferenceList)]
        [TestCase(ForbiddenSerializedValueKind.NullJaggedArray)]
        [TestCase(ForbiddenSerializedValueKind.EmptyJaggedArray)]
        [TestCase(ForbiddenSerializedValueKind.EmptyNestedList)]
        [TestCase(ForbiddenSerializedValueKind.NullUnityObjectArray)]
        [TestCase(ForbiddenSerializedValueKind.EmptyDelegateArray)]
        [TestCase(ForbiddenSerializedValueKind.NullInlineObject)]
        [TestCase(ForbiddenSerializedValueKind.NullList)]
        [TestCase(ForbiddenSerializedValueKind.NullString)]
        [TestCase(ForbiddenSerializedValueKind.LongBackedEnum)]
        [TestCase(ForbiddenSerializedValueKind.SerializeReferenceValueType)]
        [TestCase(ForbiddenSerializedValueKind.SerializeReferenceString)]
        public void ForbiddenSerializedValuesAreRejectedBeforeTheFreezerRuns(
            ForbiddenSerializedValueKind kind)
        {
            CoCoStateConfig config = CreateForbiddenSerializedConfig(kind);

            CoCoStateGraphAssetCompileResult result = Compile(config);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.IsTrue(HasAuthoringEnvelopeError(result));
        }

        private CoCoStateGraphAssetCompileResult Compile(CoCoStateConfig config)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            try
            {
                Assert.IsTrue(asset.EnsureAssetIdentity("authoring-envelope-test-guid"));
                CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
                CoCoStateGraphAuthoringOperations.AddState(
                    asset,
                    layerId,
                    default,
                    CoCoStateGraphTestFactory.StateDescriptorId,
                    config,
                    "State");
                return new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(asset, catalog);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private CoCoStateGraphAssetCompileResult CompileSharedAcrossTwoStates(CoCoStateConfig config)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            try
            {
                Assert.IsTrue(asset.EnsureAssetIdentity("shared-config-envelope-test-guid"));
                CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
                CoCoStateGraphAuthoringOperations.AddState(
                    asset,
                    layerId,
                    default,
                    CoCoStateGraphTestFactory.StateDescriptorId,
                    config,
                    "First");
                CoCoStateGraphAuthoringOperations.AddState(
                    asset,
                    layerId,
                    default,
                    CoCoStateGraphTestFactory.StateDescriptorId,
                    config,
                    "Second");
                return new CoCoStateGraphAssetCompiler(new CoCoStateGraphCompilationCache())
                    .Compile(asset, catalog);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static bool HasAuthoringEnvelopeError(CoCoStateGraphAssetCompileResult result) =>
            result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidAuthoringDependency &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.State &&
                diagnostic.Location.Field == CoCoGraphField.Config);

        private static CoCoStateConfig CreateNonSerializedConfig(NonSerializedStateKind kind)
        {
            switch (kind)
            {
                case NonSerializedStateKind.Delegate:
                    return new NonSerializedDelegateConfig();
                case NonSerializedStateKind.Dictionary:
                    return new NonSerializedDictionaryConfig();
                case NonSerializedStateKind.UnityObject:
                    return new NonSerializedUnityObjectConfig();
                case NonSerializedStateKind.PrivateField:
                    return new PrivateRuntimeStateConfig();
                case NonSerializedStateKind.ReadonlyField:
                    return new ReadonlyRuntimeStateConfig();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static CoCoStateConfig CreateForbiddenSerializedConfig(
            ForbiddenSerializedValueKind kind)
        {
            switch (kind)
            {
                case ForbiddenSerializedValueKind.Delegate:
                    return new SerializedDelegateConfig();
                case ForbiddenSerializedValueKind.Dictionary:
                    return new SerializedDictionaryConfig();
                case ForbiddenSerializedValueKind.UnityObject:
                    return new SerializedUnityObjectConfig();
                case ForbiddenSerializedValueKind.MultidimensionalArray:
                    return new MultidimensionalArrayConfig();
                case ForbiddenSerializedValueKind.JaggedArray:
                    return new JaggedArrayConfig();
                case ForbiddenSerializedValueKind.NestedList:
                    return new NestedListConfig();
                case ForbiddenSerializedValueKind.SharedSerializeReferenceList:
                    var shared = new List<ManagedReferenceNode>
                    {
                        new ManagedReferenceNode { Value = 1 }
                    };
                    return new SharedSerializeReferenceListConfig
                    {
                        First = shared,
                        Second = shared
                    };
                case ForbiddenSerializedValueKind.NullJaggedArray:
                    return new NullJaggedArrayConfig();
                case ForbiddenSerializedValueKind.EmptyJaggedArray:
                    return new EmptyJaggedArrayConfig();
                case ForbiddenSerializedValueKind.EmptyNestedList:
                    return new EmptyNestedListConfig();
                case ForbiddenSerializedValueKind.NullUnityObjectArray:
                    return new NullUnityObjectArrayConfig();
                case ForbiddenSerializedValueKind.EmptyDelegateArray:
                    return new EmptyDelegateArrayConfig();
                case ForbiddenSerializedValueKind.NullInlineObject:
                    return new NullInlineObjectConfig();
                case ForbiddenSerializedValueKind.NullList:
                    return new NullListConfig();
                case ForbiddenSerializedValueKind.NullString:
                    return new NullStringConfig();
                case ForbiddenSerializedValueKind.LongBackedEnum:
                    return new LongBackedEnumConfig();
                case ForbiddenSerializedValueKind.SerializeReferenceValueType:
                    return new SerializeReferenceValueTypeConfig();
                case ForbiddenSerializedValueKind.SerializeReferenceString:
                    return new SerializeReferenceStringConfig();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public enum NonSerializedStateKind
        {
            Delegate,
            Dictionary,
            UnityObject,
            PrivateField,
            ReadonlyField
        }

        public enum ForbiddenSerializedValueKind
        {
            Delegate,
            Dictionary,
            UnityObject,
            MultidimensionalArray,
            JaggedArray,
            NestedList,
            SharedSerializeReferenceList,
            NullJaggedArray,
            EmptyJaggedArray,
            EmptyNestedList,
            NullUnityObjectArray,
            EmptyDelegateArray,
            NullInlineObject,
            NullList,
            NullString,
            LongBackedEnum,
            SerializeReferenceValueType,
            SerializeReferenceString
        }

        [Serializable]
        private sealed class NonSerializedDelegateConfig : CoCoStateConfig
        {
            [NonSerialized] public Action Callback = () => { };
        }

        [Serializable]
        private sealed class NonSerializedDictionaryConfig : CoCoStateConfig
        {
            [NonSerialized] public Dictionary<int, int> Values = new Dictionary<int, int>();
        }

        [Serializable]
        private sealed class NonSerializedUnityObjectConfig : CoCoStateConfig
        {
            [NonSerialized] public UnityEngine.Object Reference;
        }

        [Serializable]
        private sealed class PrivateRuntimeStateConfig : CoCoStateConfig
        {
#pragma warning disable CS0414
            private int hiddenValue = 1;
#pragma warning restore CS0414
        }

        [Serializable]
        private sealed class ReadonlyRuntimeStateConfig : CoCoStateConfig
        {
            public readonly int HiddenValue = 1;
        }

        [Serializable]
        private sealed class SerializedDelegateConfig : CoCoStateConfig
        {
            public Action Callback = () => { };
        }

        [Serializable]
        private sealed class SerializedDictionaryConfig : CoCoStateConfig
        {
            public Dictionary<int, int> Values = new Dictionary<int, int>();
        }

        [Serializable]
        private sealed class SerializedUnityObjectConfig : CoCoStateConfig
        {
            public UnityEngine.Object Reference;
        }

        [Serializable]
        private sealed class MultidimensionalArrayConfig : CoCoStateConfig
        {
            public int[,] Values = new int[1, 1];
        }

        [Serializable]
        private sealed class JaggedArrayConfig : CoCoStateConfig
        {
            public int[][] Values = { new[] { 1 } };
        }

        [Serializable]
        private sealed class NestedListConfig : CoCoStateConfig
        {
            public List<List<int>> Values = new List<List<int>>
            {
                new List<int> { 1 }
            };
        }

        [Serializable]
        private sealed class ManagedReferenceNode
        {
            public int Value;
        }

        [Serializable]
        private sealed class SharedSerializeReferenceListConfig : CoCoStateConfig
        {
            [SerializeReference] public List<ManagedReferenceNode> First;
            [SerializeReference] public List<ManagedReferenceNode> Second;
        }

        [Serializable]
        private sealed class InlineLeaf
        {
            public int Value;
        }

        [Serializable]
        private sealed class InlineAliasConfig : CoCoStateConfig
        {
            public InlineLeaf First = new InlineLeaf();
            public InlineLeaf Second = new InlineLeaf();
        }

        [Serializable]
        private class InlineBase
        {
            public int Value;
        }

        [Serializable]
        private sealed class InlineDerived : InlineBase
        {
            public int ExtraValue;
        }

        [Serializable]
        private sealed class PolymorphicInlineConfig : CoCoStateConfig
        {
            public InlineBase Value = new InlineBase();
        }

        [Serializable]
        private sealed class CyclicNode
        {
            public int Value;
            public CyclicNode Next;
        }

        [Serializable]
        private sealed class CyclicConfig : CoCoStateConfig
        {
            public CyclicNode Value;
        }

        [Serializable]
        private sealed class NullJaggedArrayConfig : CoCoStateConfig
        {
            public int[][] Values;
        }

        [Serializable]
        private sealed class EmptyJaggedArrayConfig : CoCoStateConfig
        {
            public int[][] Values = Array.Empty<int[]>();
        }

        [Serializable]
        private sealed class EmptyNestedListConfig : CoCoStateConfig
        {
            public List<List<int>> Values = new List<List<int>>();
        }

        [Serializable]
        private sealed class NullUnityObjectArrayConfig : CoCoStateConfig
        {
            public UnityEngine.Object[] Values;
        }

        [Serializable]
        private sealed class EmptyDelegateArrayConfig : CoCoStateConfig
        {
            public Action[] Values = Array.Empty<Action>();
        }

        [Serializable]
        private sealed class NullInlineObjectConfig : CoCoStateConfig
        {
            public InlineLeaf Value;
        }

        [Serializable]
        private sealed class NullListConfig : CoCoStateConfig
        {
            public List<int> Values;
        }

        [Serializable]
        private sealed class NullStringConfig : CoCoStateConfig
        {
            public string Value;
        }

        private enum LongBackedEnum : long
        {
            Value = long.MaxValue
        }

        [Serializable]
        private sealed class LongBackedEnumConfig : CoCoStateConfig
        {
            public LongBackedEnum Value = LongBackedEnum.Value;
        }

        [Serializable]
        private sealed class SerializeReferenceValueTypeConfig : CoCoStateConfig
        {
            [SerializeReference] public int Value = 1;
        }

        [Serializable]
        private sealed class SerializeReferenceStringConfig : CoCoStateConfig
        {
            [SerializeReference] public string Value = "value";
        }
    }
}
