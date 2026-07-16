using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public static class CoCoStateGraphFixtureCounters
    {
        public static int LogicConstructed { get; set; }
        public static int MemoryConstructed { get; set; }
        public static int ConditionConstructed { get; set; }
        public static int ReducerCreated { get; set; }
        public static int OperationViewCreated { get; set; }
        public static int RebuilderCalled { get; set; }
        public static int StateFreezeCalls { get; set; }
        public static int ConditionFreezeCalls { get; set; }

        public static void Reset()
        {
            LogicConstructed = 0;
            MemoryConstructed = 0;
            ConditionConstructed = 0;
            ReducerCreated = 0;
            OperationViewCreated = 0;
            RebuilderCalled = 0;
            StateFreezeCalls = 0;
            ConditionFreezeCalls = 0;
        }
    }

    [Serializable]
    public sealed class TestStateAuthoringConfig : CoCoStateConfig
    {
        public int Value;
        public int[] Values = Array.Empty<int>();
        public List<int> Items = new List<int>();
    }

    [Serializable]
    public sealed class TestConditionAuthoringConfig : CoCoConditionConfig
    {
        public int Threshold;
    }

    [Serializable]
    public sealed class AlternateConditionAuthoringConfig : CoCoConditionConfig
    {
        public int Value;
    }

    public readonly struct TestStateConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public readonly struct TestConditionConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public readonly struct TestArrayConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public static class TestFrozenConfigSchemas
    {
        static TestFrozenConfigSchemas()
        {
            var stateBuilder = new CoCoFrozenConfigSchemaBuilder<TestStateConfigSchema>();
            StateValue = AddField(stateBuilder, 1UL);
            StateSchema = Freeze(stateBuilder);

            var conditionBuilder = new CoCoFrozenConfigSchemaBuilder<TestConditionConfigSchema>();
            ConditionThreshold = AddField(conditionBuilder, 2UL);
            ConditionSchema = Freeze(conditionBuilder);

            var arrayBuilder = new CoCoFrozenConfigSchemaBuilder<TestArrayConfigSchema>();
            ArrayValues = AddArrayField(arrayBuilder, 3UL);
            ArraySchema = Freeze(arrayBuilder);
        }

        public static readonly CoCoFrozenConfigField<TestStateConfigSchema, int> StateValue;
        public static readonly CoCoFrozenConfigSchema<TestStateConfigSchema> StateSchema;
        public static readonly CoCoFrozenConfigField<TestConditionConfigSchema, int> ConditionThreshold;
        public static readonly CoCoFrozenConfigSchema<TestConditionConfigSchema> ConditionSchema;
        public static readonly CoCoFrozenConfigArrayField<TestArrayConfigSchema, int> ArrayValues;
        public static readonly CoCoFrozenConfigSchema<TestArrayConfigSchema> ArraySchema;

        private static CoCoFrozenConfigField<TSchema, int> AddField<TSchema>(
            CoCoFrozenConfigSchemaBuilder<TSchema> builder,
            ulong low)
            where TSchema : struct, ICoCoFrozenConfigSchema
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(1UL, low, out CoCoFrozenConfigFieldId id) ||
                !builder.TryAddField(
                    id,
                    out CoCoFrozenConfigField<TSchema, int> field,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return field;
        }

        private static CoCoFrozenConfigArrayField<TSchema, int> AddArrayField<TSchema>(
            CoCoFrozenConfigSchemaBuilder<TSchema> builder,
            ulong low)
            where TSchema : struct, ICoCoFrozenConfigSchema
        {
            CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(1UL, low, out CoCoFrozenConfigFieldId id) ||
                !builder.TryAddArrayField(
                    id,
                    out CoCoFrozenConfigArrayField<TSchema, int> field,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return field;
        }

        private static CoCoFrozenConfigSchema<TSchema> Freeze<TSchema>(
            CoCoFrozenConfigSchemaBuilder<TSchema> builder)
            where TSchema : struct, ICoCoFrozenConfigSchema
        {
            if (!builder.TryFreeze(
                    out CoCoFrozenConfigSchema<TSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return schema;
        }
    }

    public sealed class TestStateConfigFreezer :
        ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            CoCoStateGraphFixtureCounters.StateFreezeCalls++;
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

    public sealed class TestConditionConfigFreezer :
        ICoCoConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema>
    {
        public bool TryFreeze(
            TestConditionAuthoringConfig source,
            CoCoFrozenConfigWriter<TestConditionConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            CoCoStateGraphFixtureCounters.ConditionFreezeCalls++;
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Condition config is required.");
                return false;
            }

            return writer.TryWrite(
                TestFrozenConfigSchemas.ConditionThreshold,
                source.Threshold,
                out diagnostic);
        }
    }

    public sealed class TestArrayConfigFreezer :
        ICoCoConfigFreezer<TestStateAuthoringConfig, TestArrayConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            CoCoFrozenConfigWriter<TestArrayConfigSchema> writer,
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
        ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            throw new InvalidOperationException("Synthetic State freezer failure.");
        }
    }

    public sealed class FalseWithoutDiagnosticStateConfigFreezer :
        ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return false;
        }
    }

    public sealed class MutableFailureStateConfigFreezer :
        ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public string FailureMessage { get; set; } = "Initial synthetic failure.";

        public bool TryFreeze(
            TestStateAuthoringConfig source,
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                FailureMessage);
            return false;
        }
    }

    public sealed class ThrowingConditionConfigFreezer :
        ICoCoConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema>
    {
        public bool TryFreeze(
            TestConditionAuthoringConfig source,
            CoCoFrozenConfigWriter<TestConditionConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            throw new InvalidOperationException("Synthetic Condition freezer failure.");
        }
    }

    public sealed class FalseWithoutDiagnosticConditionConfigFreezer :
        ICoCoConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema>
    {
        public bool TryFreeze(
            TestConditionAuthoringConfig source,
            CoCoFrozenConfigWriter<TestConditionConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return false;
        }
    }

    public sealed class TestStateLogic : CoCoStateLogic
    {
        public TestStateLogic()
        {
            CoCoStateGraphFixtureCounters.LogicConstructed++;
        }
    }

    public sealed class TestActivationMemory : CoCoActivationMemory
    {
        public TestActivationMemory()
        {
            CoCoStateGraphFixtureCounters.MemoryConstructed++;
        }
    }

    public sealed class TestStateCondition : CoCoStateCondition
    {
        public TestStateCondition()
        {
            CoCoStateGraphFixtureCounters.ConditionConstructed++;
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

    public readonly struct TestIntentReducer : ICoCoIntentReducer<TestIntent>
    {
        public TestIntent Reduce(in TestIntent current, in TestIntent candidate) =>
            new TestIntent(current.Value + candidate.Value);
    }

    public sealed class TestIntentReducerFactory :
        ICoCoIntentReducerFactory<TestIntent, TestIntentReducer>
    {
        public TestIntentReducer Create(CoCoGraphInstanceId graphInstanceId)
        {
            CoCoStateGraphFixtureCounters.ReducerCreated++;
            return new TestIntentReducer();
        }
    }

    public interface ITestOperationSection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public sealed class TestOperationSectionView : ITestOperationSection
    {
        public int Value => 0;
    }

    public sealed class TestOperationSectionViewFactory :
        ICoCoOperationSectionViewFactory<ITestOperationSection>
    {
        public ITestOperationSection Create(
            in CoCoOperationSectionViewContext<ITestOperationSection> context)
        {
            CoCoStateGraphFixtureCounters.OperationViewCreated++;
            return new TestOperationSectionView();
        }
    }

    public sealed class TestDerivedStateRebuilder : ICoCoDerivedStateRebuilder<int>
    {
        public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
        {
            CoCoStateGraphFixtureCounters.RebuilderCalled++;
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
        ICoCoDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        public bool TryRebuild(in CoCoDerivedStateReadContext context, out TValue value)
        {
            value = default;
            return true;
        }
    }
}
