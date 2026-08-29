using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Deterministic descriptor ids for CoCoStateAttribute-declared states.
    /// Derived from the stable package scope and the attribute name hash so
    /// graph assets and registrations never need hand-maintained ids.
    /// </summary>
    public static class StandardDescriptors
    {
        private const ulong High = 0x434F434F53544154UL; // "COCOSTAT"

        private static readonly Dictionary<CoCoStateDescriptorId, Type> TableValue =
            new Dictionary<CoCoStateDescriptorId, Type>();

        public static IReadOnlyDictionary<CoCoStateDescriptorId, Type> Table => TableValue;

        public static bool TryCreate(
            Type logicType,
            string name,
            out CoCoStateDescriptorId descriptorId)
        {
            ulong low = DeriveLow(name);
            if (!CoCoStateDescriptorId.TryCreate(High, low, out descriptorId))
            {
                return false;
            }

            TableValue[descriptorId] = logicType;
            return true;
        }

        private static ulong DeriveLow(string name)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL; // FNV-1a offset
                for (int index = 0; index < name.Length; index++)
                {
                    hash ^= (byte)name[index];
                    hash *= 1099511628211UL; // FNV-1a prime
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// Deterministic graph-state block and slot ids for standard states. Each
    /// descriptor owns one block so a compiled Graph carries only the State
    /// records it actually uses, never every attributed State in an assembly.
    /// </summary>
    public static class StandardGraphState
    {
        private const ulong High = 0x434F434F53544154UL; // "COCOSTAT"
        private const ulong BlockSalt = 0x424C4F434BUL; // "BLOCK"

        private static readonly Dictionary<CoCoStateDescriptorId, CoCoStateBlockId>
            Blocks = new Dictionary<CoCoStateDescriptorId, CoCoStateBlockId>();
        private static readonly Dictionary<CoCoStateDescriptorId, CoCoStateSlotId>
            Slots = new Dictionary<CoCoStateDescriptorId, CoCoStateSlotId>();

        public static CoCoStateBlockId BlockFor(CoCoStateDescriptorId descriptorId)
        {
            if (Blocks.TryGetValue(descriptorId, out CoCoStateBlockId cached))
            {
                return cached;
            }

            ulong low = DeriveLow(descriptorId, BlockSalt);
            if (!CoCoStateBlockId.TryCreate(High, low, out CoCoStateBlockId block))
            {
                _ = CoCoStateBlockId.TryCreate(High, 40UL, out block);
            }

            Blocks[descriptorId] = block;
            return block;
        }

        /// <summary>
        /// One derived slot per state descriptor. Catalog registration and
        /// Host binding derive the block and slot from the same descriptor,
        /// keeping the compiled manifest in sync.
        /// </summary>
        public static CoCoStateSlotId SlotFor(CoCoStateDescriptorId descriptorId)
        {
            if (Slots.TryGetValue(descriptorId, out CoCoStateSlotId cached))
            {
                return cached;
            }

            ulong low = DeriveLow(descriptorId, 0UL);
            if (!CoCoStateSlotId.TryCreate(High, low, out CoCoStateSlotId slot))
            {
                _ = CoCoStateSlotId.TryCreate(High, 41UL, out slot);
            }

            Slots[descriptorId] = slot;
            return slot;
        }

        private static ulong DeriveLow(
            CoCoStateDescriptorId descriptorId,
            ulong salt)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                ulong value = descriptorId.High ^ descriptorId.Low ^ salt;
                for (int bit = 0; bit < 64; bit += 8)
                {
                    hash ^= (value >> bit) & 0xFFUL;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// Empty authored configuration for states without config fields. One
    /// shared implementation for every standard state.
    /// </summary>
    [Serializable]
    public sealed class EmptyStateConfig : CoCoStateConfig
    {
        public sealed class Freezer :
            ICoCoConfigFreezer<EmptyStateConfig, EmptyConfigSchema>
        {
            public bool TryFreeze(
                EmptyStateConfig source,
                CoCoFrozenConfigWriter<EmptyConfigSchema> writer,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = source != null
                    ? CoCoDiagnostic.None
                    : CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.InvalidFrozenConfig,
                        "State config is required.");
                return source != null;
            }
        }
    }

    public readonly struct EmptyConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public static class EmptySchemas
    {
        static EmptySchemas()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<EmptyConfigSchema>();
            _ = builder.TryFreeze(
                out CoCoFrozenConfigSchema<EmptyConfigSchema> schema,
                out _);
            State = schema;
        }

        public static readonly CoCoFrozenConfigSchema<EmptyConfigSchema> State;
    }

    /// <summary>
    /// Activation memory for states without memory fields: no data, fixed
    /// fingerprint, always capturable and restorable.
    /// </summary>
    public sealed class StatelessMemory : CoCoActivationMemory
    {
        public const ulong Fingerprint = 40503UL;
    }

    public sealed class StatelessMemoryBinding :
        ICoCoActivationMemoryStateBinding<StatelessMemory, int>
    {
        public ulong SemanticFingerprint => StatelessMemory.Fingerprint;

        public bool TryCapture(
            StatelessMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            state = 0;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in int state,
            StatelessMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    /// <summary>
    /// Non-generic runtime registration for standard states: empty config
    /// schema, stateless memory.
    /// </summary>
    public sealed class StandardStateRegistration : CoCoStateRuntimeRegistration
    {
        public StandardStateRegistration(Type logicType)
            : base(
                logicType,
                typeof(EmptyConfigSchema),
                EmptySchemas.State.Fingerprint,
                typeof(StatelessMemory),
                false)
        {
        }
    }
}
