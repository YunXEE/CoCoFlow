using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 资产在节点上保存 StateConfig，目录通过相应 freezer 冻结配置。无字段节点使用标准空配置。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>供没有节点配置字段的 Logic 使用的标准空配置。</summary>
    [Serializable]
    public sealed class EmptyStateConfig : StateConfig
    {
        /// <summary>把标准空状态配置按空 schema 冻结。</summary>
        public sealed class Freezer :
            IConfigFreezer<EmptyStateConfig, EmptyConfigSchema>
        {
            /// <summary>接受空配置的写入合同，供没有节点字段的状态建立冻结配置。</summary>
            public bool TryFreeze(
                EmptyStateConfig source,
                FrozenConfigWriter<EmptyConfigSchema> writer,
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

    /// <summary>标记标准空配置所使用的字段布局。</summary>
    public readonly struct EmptyConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>保存标准空配置的共享冻结 schema。</summary>
    public static class EmptySchemas
    {

        #region Public API

        public static readonly FrozenConfigSchema<EmptyConfigSchema> State;

        #endregion

        #region Internal Logic

        static EmptySchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<EmptyConfigSchema>();
            _ = builder.TryFreeze(
                out FrozenConfigSchema<EmptyConfigSchema> schema,
                out _);
            State = schema;
        }

        #endregion
    }

    /// <summary>图节点保存的项目配置基类，Host 经注册的 freezer 把它转成只读值。</summary>
    [Serializable]
    public abstract class StateConfig
    {
    }
}
