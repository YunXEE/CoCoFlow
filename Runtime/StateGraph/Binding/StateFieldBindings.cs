using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using Unity.Collections.LowLevel.Unsafe;

// 标准 Provider 按带配置与记忆标记的字段选择冻结器、记忆复制方式和项目工厂。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>扫描项目状态与依赖，向图编译器提供目录并向 Host 提供实例工厂。</summary>
    internal static class StateFieldBindings
    {
        internal static bool ProvidesActionProgress(Type type) =>
            ((StateAttribute)Attribute.GetCustomAttribute(type, typeof(StateAttribute)))
                ?.ProvidesActionProgress ?? false;

        internal static bool HasFields(Type type) =>
            StateFields.Fields(type, typeof(StateConfigAttribute)).Length != 0 ||
            StateFields.Fields(type, typeof(StateMemoryAttribute)).Length != 0;

        internal static object InvokeFields(string method, Type logic, params object[] args) =>
            typeof(StateFieldBindings).GetMethod(method,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .MakeGenericMethod(logic, StateFields.StoredType(logic)).Invoke(null, args);

        private static unsafe FieldMemoryTransfer<TState> FieldTransfer<TState>(Type logicType) where TState : unmanaged
        {
            var fields = StateFields.Fields(logicType, typeof(StateMemoryAttribute));
            var layout = new (int Logic, int Memory, int Size)[fields.Length];
            Type cursor = typeof(TState);
            int offset = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                layout[i] = (UnsafeUtility.GetFieldOffset(fields[i]),
                    offset + UnsafeUtility.GetFieldOffset(cursor.GetField("Item1")), UnsafeUtility.SizeOf(fields[i].FieldType));
                offset += UnsafeUtility.GetFieldOffset(cursor.GetField("Item2"));
                cursor = cursor.GetGenericArguments()[1];
            }
            return (StateLogic logic, ref TState value, bool load) =>
            {
                if (layout.Length == 0) return;
                byte* address = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(logic, out ulong handle);
                try
                {
                    fixed (TState* memory = &value)
                    foreach (var field in layout)
                    {
                        byte* member = address + field.Logic;
                        byte* stored = (byte*)memory + field.Memory;
                        UnsafeUtility.MemCpy(load ? member : stored, load ? stored : member, field.Size);
                    }
                }
                finally { UnsafeUtility.ReleaseGCObject(handle); }
            };
        }

        internal static bool RegisterFields<TLogic, TState>(GraphDescriptorCatalogBuilder builder,
            StateDescriptorId descriptorId, IntentId[] consumedIntents,
            OperationId[] providedSections, StateBlockId[] contextBlocks)
            where TLogic : StateLogic, IStateUpdate, new() where TState : unmanaged
        {
            FieldMemory<TLogic, TState>.Transfer ??= FieldTransfer<TState>(typeof(TLogic));
            var block = StandardGraphState.BlockFor(descriptorId);
            return builder.TryRegisterStateBlock(block, StateBlockOwner.Graph, out _) &&
                builder.TryRegisterState<TLogic, FieldConfig, FieldConfigSchema, FieldMemory<TLogic, TState>>(
                    descriptorId, 1, new FieldConfig.Freezer(typeof(TLogic)),
                    new StateRuntimeRegistration<TLogic, FieldConfigSchema, FieldMemory<TLogic, TState>>(
                        FieldConfigSchema.Schema, ProvidesActionProgress(typeof(TLogic))),
                    consumedIntents, providedSections, contextBlocks, out _) &&
                builder.TryRegisterStateSlot<GraphStateRecord<TState>>(block, StandardGraphState.SlotFor(descriptorId),
                    ContextProjection.Temporal | ContextProjection.Durable, ContextRestorePolicy.Stored,
                    default, new FieldMemory<TLogic, TState>.Binding().SemanticFingerprint, default, null, out _);
        }

        internal static IStateRuntimeFactory FieldFactory<TLogic, TState>()
            where TLogic : StateLogic, IStateUpdate, new() where TState : unmanaged =>
            new StateRuntimeFactory<TLogic, FieldMemory<TLogic, TState>>(
                context =>
                {
                    var logic = new TLogic();
                    context.Config.TryReadArray(FieldConfigSchema.Values, out var values);
                    new FieldConfig { Values = values.ToArray() }.Apply(logic);
                    return logic;
                },
                () => new FieldMemory<TLogic, TState>(),
                (source, destination) => destination.Value = source.Value,
                memory => memory.Value = FieldMemory<TLogic, TState>.Initial,
                FieldMemory<TLogic, TState>.Fingerprint);

        internal static bool BindFields<TLogic, TState>(HostBindingBuilder builder,
            CompiledStateLayer layer, CompiledState state, out CoCoDiagnostic diagnostic)
            where TLogic : StateLogic, IStateUpdate, new() where TState : unmanaged
        {
            var binding = new FieldMemory<TLogic, TState>.Binding();
            ulong initial = InitialActivation(builder.Graph, state);
            ActivationId.TryCreate(initial, out var activation);
            bool isOnActivePath = initial != 0 && !state.IsAnyState;
            GraphStateRecord<TState>.TryCreate(layer.LayerId, state.StateId, isOnActivePath, activation,
                0, 0, isOnActivePath, binding.SemanticFingerprint, FieldMemory<TLogic, TState>.Initial, out var record);
            return builder.TryBindGraphStateSlot<FieldMemory<TLogic, TState>, TState, FieldMemory<TLogic, TState>.Binding>(
                layer.LayerId, state.StateId, StandardGraphState.BlockFor(state.Descriptor.DescriptorId),
                StandardGraphState.SlotFor(state.Descriptor.DescriptorId), record, binding.SemanticFingerprint, binding, out diagnostic);
        }

        internal static ulong InitialActivation(CompiledStateGraph graph, CompiledState state)
        {
            ulong activation = 0;
            foreach (var layer in graph.Layers)
            {
                int leaf = layer.InitialStateIndex;
                while (layer.States[leaf].InitialChildStateIndex >= 0)
                    leaf = layer.States[leaf].InitialChildStateIndex;
                foreach (int index in layer.States[leaf].RootPathStateIndices)
                {
                    activation++;
                    if (ReferenceEquals(layer.States[index], state)) return activation;
                }
                if (layer.AnyStateIndex >= 0)
                {
                    activation++;
                    if (ReferenceEquals(layer.States[layer.AnyStateIndex], state)) return activation;
                }
            }
            return 0;
        }
    }
}
