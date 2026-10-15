using System;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;

namespace CoCoFlow.Runtime.Core
{
    public sealed partial class CoCoStandardBindingProvider
    {
        private static bool HasFields(Type type) =>
            CoCoStateFields.Fields(type, typeof(CoCoStateConfigAttribute)).Length != 0 ||
            CoCoStateFields.Fields(type, typeof(CoCoStateMemoryAttribute)).Length != 0;

        private static object InvokeFields(string method, Type logic, params object[] args) =>
            typeof(CoCoStandardBindingProvider).GetMethod(method,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .MakeGenericMethod(logic, CoCoStateFields.StoredType(logic)).Invoke(null, args);

        private static unsafe CoCoFieldMemoryTransfer<TState> FieldTransfer<TState>(Type logicType) where TState : unmanaged
        {
            var fields = CoCoStateFields.Fields(logicType, typeof(CoCoStateMemoryAttribute));
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
            return (CoCoStateLogic logic, ref TState value, bool load) =>
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

        private static bool RegisterFields<TLogic, TState>(CoCoGraphDescriptorCatalogBuilder builder,
            CoCoStateDescriptorId descriptorId, CoCoIntentId[] consumedIntents,
            CoCoOperationSectionId[] providedSections, CoCoStateBlockId[] contextBlocks)
            where TLogic : CoCoStateLogic, ICoCoStateUpdate, new() where TState : unmanaged
        {
            CoCoFieldMemory<TLogic, TState>.Transfer ??= FieldTransfer<TState>(typeof(TLogic));
            var block = StandardGraphState.BlockFor(descriptorId);
            return builder.TryRegisterStateBlock(block, CoCoStateBlockOwner.Graph, out _) &&
                builder.TryRegisterState<TLogic, CoCoFieldConfig, CoCoFieldConfigSchema, CoCoFieldMemory<TLogic, TState>>(
                    descriptorId, 1, new CoCoFieldConfig.Freezer(typeof(TLogic)),
                    new CoCoStateRuntimeRegistration<TLogic, CoCoFieldConfigSchema, CoCoFieldMemory<TLogic, TState>>(
                        CoCoFieldConfigSchema.Schema, ProvidesActionProgress(typeof(TLogic))),
                    consumedIntents, providedSections, contextBlocks, out _) &&
                builder.TryRegisterStateSlot<CoCoGraphStateRecord<TState>>(block, StandardGraphState.SlotFor(descriptorId),
                    CoCoContextProjection.Temporal | CoCoContextProjection.Durable, CoCoContextRestorePolicy.Stored,
                    default, new CoCoFieldMemory<TLogic, TState>.Binding().SemanticFingerprint, default, null, out _);
        }

        private static ICoCoStateRuntimeFactory FieldFactory<TLogic, TState>()
            where TLogic : CoCoStateLogic, ICoCoStateUpdate, new() where TState : unmanaged =>
            new CoCoStateRuntimeFactory<TLogic, CoCoFieldMemory<TLogic, TState>>(
                context =>
                {
                    var logic = new TLogic();
                    context.Config.TryReadArray(CoCoFieldConfigSchema.Values, out var values);
                    new CoCoFieldConfig { Values = values.ToArray() }.Apply(logic);
                    return logic;
                },
                () => new CoCoFieldMemory<TLogic, TState>(),
                (source, destination) => destination.Value = source.Value,
                memory => memory.Value = CoCoFieldMemory<TLogic, TState>.Initial,
                CoCoFieldMemory<TLogic, TState>.Fingerprint);

        private static bool BindFields<TLogic, TState>(CoCoStateGraphHostBindingBuilder builder,
            CoCoCompiledStateLayer layer, CoCoCompiledState state, out CoCoDiagnostic diagnostic)
            where TLogic : CoCoStateLogic, ICoCoStateUpdate, new() where TState : unmanaged
        {
            var binding = new CoCoFieldMemory<TLogic, TState>.Binding();
            ulong initial = InitialActivation(builder.Graph, state);
            CoCoActivationId.TryCreate(initial, out var activation);
            CoCoGraphStateRecord<TState>.TryCreate(layer.LayerId, state.StateId, initial != 0, activation,
                0, 0, initial != 0, binding.SemanticFingerprint, CoCoFieldMemory<TLogic, TState>.Initial, out var record);
            return builder.TryBindGraphStateSlot<CoCoFieldMemory<TLogic, TState>, TState, CoCoFieldMemory<TLogic, TState>.Binding>(
                layer.LayerId, state.StateId, StandardGraphState.BlockFor(state.Descriptor.DescriptorId),
                StandardGraphState.SlotFor(state.Descriptor.DescriptorId), record, binding.SemanticFingerprint, binding, out diagnostic);
        }

        private static ulong InitialActivation(CoCoCompiledStateGraph graph, CoCoCompiledState state)
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
            }
            return 0;
        }
    }
}
