using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 目录记录 Logic、Memory 与配置 schema，Host 根据该记录安装每个状态的实例工厂。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将运行时 Logic、Memory 与配置 schema 绑定到一个状态注册。</summary>
    public abstract class StateRuntimeRegistration
    {
        /// <summary>关联 Logic、记忆类型和配置 schema 供目录验证。泛型注册从冻结的 schema 取得指纹。</summary>
        protected StateRuntimeRegistration(
            Type logicType,
            Type configSchemaType,
            ulong configSchemaFingerprint,
            Type activationMemoryType,
            bool providesActionProgress)
        {
            LogicType = logicType;
            ConfigSchemaType = configSchemaType;
            ConfigSchemaFingerprint = configSchemaFingerprint;
            ActivationMemoryType = activationMemoryType;
            ProvidesActionProgress = providesActionProgress;
        }

        public Type LogicType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
        public Type ActivationMemoryType { get; }
        public bool ProvidesActionProgress { get; }
    }

    /// <summary>将运行时 Logic、Memory 与配置 schema 绑定到一个状态注册。</summary>
    public sealed class StateRuntimeRegistration<TLogic, TSchema, TMemory> :
        StateRuntimeRegistration
        where TLogic : StateLogic
        where TSchema : struct, IFrozenConfigSchema
        where TMemory : StateActivationMemory
    {

        #region Public API

        public FrozenConfigSchema<TSchema> ConfigSchema { get; }
        /// <summary>关联 Logic、记忆类型和配置 schema 供目录验证。泛型注册从冻结的 schema 取得指纹。</summary>
        public StateRuntimeRegistration(
            FrozenConfigSchema<TSchema> configSchema,
            bool providesActionProgress = false)
            : base(
                typeof(TLogic),
                typeof(TSchema),
                RequireSchema(configSchema).Fingerprint,
                typeof(TMemory),
                providesActionProgress)
        {
            ConfigSchema = configSchema;
        }

        #endregion

        #region Internal Logic

        private static FrozenConfigSchema<TSchema> RequireSchema(
            FrozenConfigSchema<TSchema> schema) =>
            schema ?? throw new ArgumentNullException(nameof(schema));

        #endregion
    }

    /// <summary>为标准属性声明的玩法状态提供目录所需的运行时登记信息。</summary>
    public sealed class StandardStateRegistration : StateRuntimeRegistration
    {
        /// <summary>将标准 Logic 类型与配置指纹、记忆类型和进度合同关联，供标准 Provider 装配。</summary>
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
