using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 图编译与创作测试用这里的真实 Logic、配置、freezer、操作和输入构造目录及失败用例。
namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    /// <summary>把一组起止值作为标准配置的结构体元素，供集合编辑与冻结检查。</summary>
    [Serializable]
    public struct StandardConfigWindow
    {
        public float start;
    }

    /// <summary>暴露可编辑的标量、结构体与集合字段，供节点草稿保存和撤销用例使用。</summary>
    public sealed class StandardConfigDraftLogic : StateLogic, IStateUpdate
    {
        [Config] public string ClipKey = "Idle";
        [Config] public float speed;
        [Config] public StandardConfigWindow[] windows = Array.Empty<StandardConfigWindow>();
        [Config] public List<float> samples = new List<float>();
        /// <summary>保留一个可用于图编译的更新回调，字段值由编辑器草稿而非帧逻辑改动。</summary>
        public void Update(StateExecutionContext context) { }
    }

    /// <summary>记录测试 freezer、归并器和重建器被实际调用的次数。</summary>
    public static class StateGraphFixtureCounters
    {
        public static int LogicConstructed { get; set; }
        public static int MemoryConstructed { get; set; }
        public static int ReducerCreated { get; set; }
        public static int OperationViewCreated { get; set; }
        public static int RebuilderCalled { get; set; }
        public static int StateFreezeCalls { get; set; }

        /// <summary>将迭代位置恢复到第一个节点之前，允许再次遍历当前活动路径。</summary>
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

    /// <summary>为图编译和资产共享引用用例提供带整数值的项目配置。</summary>
    [Serializable]
    public sealed class TestStateAuthoringConfig : StateConfig
    {
        public int Value;
        public int[] Values = Array.Empty<int>();
        public List<int> Items = new List<int>();
    }

    /// <summary>描述上述单值项目配置的冻结字段。</summary>
    public readonly struct TestStateConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>描述数组配置的冻结元素，供类型与字段种类负例使用。</summary>
    public readonly struct TestArrayConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>供图和目录夹具共用可验证的标量、数组 schema 及字段句柄。</summary>
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

    /// <summary>将项目的整数配置写入匹配字段，并记录实际冻结次数。</summary>
    public sealed class TestStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        /// <summary>计算冻结次数并写入整数配置，源无效或 writer 拒绝时返回失败诊断。</summary>
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

    /// <summary>将测试数组保存到对应 schema 的冻结数组字段。</summary>
    public sealed class TestArrayConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestArrayConfigSchema>
    {
        /// <summary>将源配置的数组写到匹配的冻结字段，返回 writer 的结果和诊断。</summary>
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

    /// <summary>主动抛出异常，使编译器的 freezer 异常处置路径可验证。</summary>
    public sealed class ThrowingStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        /// <summary>从 freezer 直接抛出测试异常，供编译器将其转换为节点诊断。</summary>
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            throw new InvalidOperationException("Synthetic State freezer failure.");
        }
    }

    /// <summary>返回失败且不提供错误诊断，供上层检查补充诊断。</summary>
    public sealed class FalseWithoutDiagnosticStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        /// <summary>返回 false 和空诊断，使测试能观察调用方产生的补充错误。</summary>
        public bool TryFreeze(
            TestStateAuthoringConfig source,
            FrozenConfigWriter<TestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return false;
        }
    }

    /// <summary>按测试控制的状态模拟同一 freezer 的成功和失败结果。</summary>
    public sealed class MutableFailureStateConfigFreezer :
        IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema>
    {
        public string FailureMessage { get; set; } = "Initial synthetic failure.";

        /// <summary>依据夹具当前配置模拟成功或失败，供验证失败不能泄漏可用的冻结快照。</summary>
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

    /// <summary>让目录与编译器用真实的 AnyState 派生类型检查常驻节点合同。</summary>
    public sealed class TestAnyStateLogic : AnyStateLogic
    {
        /// <summary>提供无副作用的常驻回调，使编译和目录测试使用实际的 AnyState 派生类型。</summary>
        public override void Update(StateExecutionContext context) { }
    }

    /// <summary>在实例化时记录测试计数，供区分目录冻结与运行对象创建。</summary>
    public sealed class TestStateLogic : StateLogic
    {
        /// <summary>增加项目 Logic 的构造计数，用来验证冻结阶段没有提前实例化。</summary>
        public TestStateLogic()
        {
            StateGraphFixtureCounters.LogicConstructed++;
        }
    }

    /// <summary>记录测试实例分配激活记忆的行为。</summary>
    public sealed class TestActivationMemory : StateActivationMemory
    {
        /// <summary>增加记忆对象的创建计数，以便对照目录冻结与实例启动。</summary>
        public TestActivationMemory()
        {
            StateGraphFixtureCounters.MemoryConstructed++;
        }
    }

    /// <summary>以一个整数作为目录和归并测试的输入载荷。</summary>
    public readonly struct TestIntent
    {
        /// <summary>保存本次贡献的整数，供归并器按序合入当前帧。</summary>
        public TestIntent(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    /// <summary>把同一步收到的多个测试整数累加为一个输入值。</summary>
    public readonly struct TestIntentReducer : IIntentReducer<TestIntent>
    {
        /// <summary>将一个来源的贡献合入当前输入，返回传给下一个来源或本步 Logic 的结果。</summary>
        public TestIntent Reduce(in TestIntent current, in TestIntent candidate) =>
            new TestIntent(current.Value + candidate.Value);
    }

    /// <summary>在分配归并器时记录计数，供检查每实例的创建时机。</summary>
    public sealed class TestIntentReducerFactory :
        IIntentReducerFactory<TestIntent, TestIntentReducer>
    {
        /// <summary>增加归并器创建计数，并为所请求实例返回新的归并器值。</summary>
        public TestIntentReducer Create(GraphInstanceId graphInstanceId)
        {
            StateGraphFixtureCounters.ReducerCreated++;
            return new TestIntentReducer();
        }
    }

    /// <summary>为目录与编译测试提供一个具有可解析字段的操作接口。</summary>
    public interface ITestOperationSection : IOperation
    {
        int Value { get; }
    }

    /// <summary>通过框架分配的字段句柄读取测试操作的值。</summary>
    public sealed class TestOperationSectionView : ITestOperationSection
    {
        public int Value => 0;
    }

    /// <summary>将测试操作的字段绑定到当前 Reader，供冻结目录及运行时访问。</summary>
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

    /// <summary>提供可计数的派生槽重建行为，供清单与恢复测试区分声明和执行。</summary>
    public sealed class TestDerivedStateRebuilder : IDerivedStateRebuilder<int>
    {
        /// <summary>从已声明依赖重新计算派生槽。失败返回 false，调用方应放弃该恢复候选。</summary>
        public bool TryRebuild(in DerivedStateReadContext context, out int value)
        {
            StateGraphFixtureCounters.RebuilderCalled++;
            value = 0;
            return true;
        }
    }

    /// <summary>保存指针宽度的数据，用来检查能否把该值纳入可移植帧布局。</summary>
    public readonly struct TestPointerSizedStateValue
    {
        /// <summary>保存指针宽度的测试值，供布局注册验证是否支持其表示。</summary>
        public TestPointerSizedStateValue(IntPtr value)
        {
            Value = value;
        }

        public IntPtr Value { get; }
    }

    /// <summary>让不支持的值类型也有可调用重建器，供校验确认先拒绝值形状。</summary>
    public sealed class TestRejectedValueStateRebuilder<TValue> :
        IDerivedStateRebuilder<TValue>
        where TValue : unmanaged
    {
        /// <summary>从已声明依赖重新计算派生槽。失败返回 false，调用方应放弃该恢复候选。</summary>
        public bool TryRebuild(in DerivedStateReadContext context, out TValue value)
        {
            value = default;
            return true;
        }
    }
}
