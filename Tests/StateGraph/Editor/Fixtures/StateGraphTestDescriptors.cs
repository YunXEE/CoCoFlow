using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    [Serializable]
    public struct StandardConfigWindow
    {
        public float start;
    }

    public sealed class StandardConfigDraftLogic : StateLogic, IStateUpdate
    {
        [Config] public string ClipKey = "Idle";
        [Config] public float speed;
        [Config] public StandardConfigWindow[] windows = Array.Empty<StandardConfigWindow>();
        [Config] public List<float> samples = new List<float>();
        public void Update(StateExecutionContext context) { }
    }

    public static class StateGraphFixtureCounters
    {
        public static int LogicConstructed { get; set; }
        public static int MemoryConstructed { get; set; }
        public static int ReducerCreated { get; set; }
        public static int OperationViewCreated { get; set; }
        public static int RebuilderCalled { get; set; }
        public static int StateFreezeCalls { get; set; }

        public static void Reset()
        {
            LogicConstructed = 0;
            MemoryConstructed = 0;
            ReducerCreated = 0;
            OperationViewCreated = 0;
            RebuilderCalled = 0;
            StateFreezeCalls = 0;
        }
    }

    [Serializable]
    public sealed class TestStateAuthoringConfig : StateConfig
    {
        public int Value;
        public int[] Values = Array.Empty<int>();
        public List<int> Items = new List<int>();
    }

    public readonly struct TestStateConfigSchema : IFrozenConfigSchema
    {
    }

    public readonly struct TestArrayConfigSchema : IFrozenConfigSchema
    {
    }

    public static class TestFrozenConfigSchemas
    {

        #region Public API

        public static readonly FrozenConfigField<TestStateConfigSchema, int> StateValue;
        public static readonly FrozenConfigSchema<TestStateConfigSchema> StateSchema;
        public static readonly FrozenConfigArrayField<TestArrayConfigSchema, int> ArrayValues;
        public static readonly FrozenConfigSchema<TestArrayConfigSchema> ArraySchema;

        #endregion

        #region Internal Logic

        static TestFrozenConfigSchemas()
        {
            var stateBuilder = new FrozenConfigSchemaBuilder<TestStateConfigSchema>();
            StateValue = AddField(stateBuilder, 1UL);
            StateSchema = Freeze(stateBuilder);

            var arrayBuilder = new FrozenConfigSchemaBuilder<TestArrayConfigSchema>();
            ArrayValues = AddArrayField(arrayBuilder, 3UL);
            ArraySchema = Freeze(arrayBuilder);
        }

        private static FrozenConfigField<TSchema, int> AddField<TSchema>(
            FrozenConfigSchemaBuilder<TSchema> builder,
            ulong low)
            where TSchema : struct, IFrozenConfigSchema
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
            if (!FrozenConfigFieldId.TryCreate(1UL, low, out FrozenConfigFieldId id) ||
                !builder.TryAddField(
                    id,
                    out FrozenConfigField<TSchema, int> field,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return field;
        }

        private static FrozenConfigArrayField<TSchema, int> AddArrayField<TSchema>(
            FrozenConfigSchemaBuilder<TSchema> builder,
            ulong low)
            where TSchema : struct, IFrozenConfigSchema
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
            if (!FrozenConfigFieldId.TryCreate(1UL, low, out FrozenConfigFieldId id) ||
                !builder.TryAddArrayField(
                    id,
                    out FrozenConfigArrayField<TSchema, int> field,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return field;
        }

        private static FrozenConfigSchema<TSchema> Freeze<TSchema>(
            FrozenConfigSchemaBuilder<TSchema> builder)
            where TSchema : struct, IFrozenConfigSchema
        {
            if (!builder.TryFreeze(
                    out FrozenConfigSchema<TSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return schema;
        }

        #endregion
    }

    public sealed class TestStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            StateGraphFixtureCounters.StateFreezeCalls++;
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "State config is required.");
                return false;
            }

            return writer.TryWrite(TestFrozenConfigSchemas.StateValue, source.Value, out diagnostic);
        }
    }

    public sealed class TestArrayConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestArrayConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestArrayConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "State config is required.");
                return false;
            }

            return writer.TryWriteArray(TestFrozenConfigSchemas.ArrayValues, source.Values, out diagnostic);
        }
    }

    public sealed class ThrowingStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            throw new InvalidOperationException("Synthetic State freezer failure.");
        }
    }

    public sealed class FalseWithoutDiagnosticStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return false;
        }
    }

    public sealed class MutableFailureStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public string FailureMessage { get; set; } = "Initial synthetic failure.";

        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                FailureMessage);
            return false;
        }
    }

    public sealed class TestAnyStateLogic : AnyStateLogic
    {
        public override void Update(StateExecutionContext context) { }
    }

    public sealed class TestStateLogic : StateLogic
    {
        public TestStateLogic()
        {
            StateGraphFixtureCounters.LogicConstructed++;
        }
    }

    public sealed class TestActivationMemory : StateActivationMemory
    {
        public TestActivationMemory()
        {
            StateGraphFixtureCounters.MemoryConstructed++;
        }
    }

    public readonly struct TestIntent
    {
        public TestIntent(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public readonly struct TestIntentReducer : IIntentReducer<TestIntent>
    {
        public TestIntent Reduce(in TestIntent current, in TestIntent candidate) =>
            new TestIntent(current.Value + candidate.Value);
    }

    public sealed class TestIntentReducerFactory :
        IIntentReducerFactory<TestIntent, TestIntentReducer>
    {
        public TestIntentReducer Create(GraphInstanceId graphInstanceId)
        {
            StateGraphFixtureCounters.ReducerCreated++;
            return new TestIntentReducer();
        }
    }

    public interface ITestOperationSection : IOperation
    {
        int Value { get; }
    }

    public sealed class TestOperationSectionView : ITestOperationSection
    {
        public int Value => 0;
    }

    public sealed class TestOperationSectionViewFactory :
        IOperationViewFactory<ITestOperationSection>
    {
        ITestOperationSection IOperationViewFactory<ITestOperationSection>.Create(
            in OperationViewContext<ITestOperationSection> context)
        {
            StateGraphFixtureCounters.OperationViewCreated++;
            return new TestOperationSectionView();
        }
    }

    public sealed class TestDerivedStateRebuilder : IDerivedStateRebuilder<int>
    {
        public bool TryRebuild(in DerivedStateReadContext context, out int value)
        {
            StateGraphFixtureCounters.RebuilderCalled++;
            value = 0;
            return true;
        }
    }

    public readonly struct TestPointerSizedStateValue
    {
        public TestPointerSizedStateValue(IntPtr value)
        {
            Value = value;
        }

        public IntPtr Value { get; }
    }

    public sealed class TestRejectedValueStateRebuilder<TValue> :
        IDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        public bool TryRebuild(in DerivedStateReadContext context, out TValue value)
        {
            value = default;
            return true;
        }
    }
}
