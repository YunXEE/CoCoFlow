using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 依赖闭包测试以这些项目描述符构造对 Unity 图程序集的间接引用并检查诊断位置。
namespace CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures
{
    /// <summary>在依赖闭包负例中用作图节点的可编辑配置。</summary>
    [Serializable]
    public sealed class AuthoringDependencyConfig : StateConfig
    {
        public int Value;
    }

    /// <summary>为含有 Unity 依赖的作者程序集提供可注册的配置 schema。</summary>
    public readonly struct AuthoringDependencyConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>为依赖检查夹具预建冻结配置的类型信息。</summary>
    public static class AuthoringDependencySchemas
    {

        #region Public API

        public static readonly FrozenConfigSchema<AuthoringDependencyConfigSchema> Schema;

        #endregion

        #region Internal Logic

        static AuthoringDependencySchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<AuthoringDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out FrozenConfigSchema<AuthoringDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        #endregion
    }

    /// <summary>使创作闭包可从实际项目状态追溯到它引用的 Unity 图程序集。</summary>
    public sealed class AuthoringDependencyLogic : StateLogic
    {
        public static Type ReferencedAuthoringType => typeof(StateGraphAsset);
    }

    /// <summary>为依赖检查用的项目状态提供可独立创建的激活记忆。</summary>
    public sealed class AuthoringDependencyMemory : StateActivationMemory
    {
    }

    /// <summary>将该负例的节点配置交给冻结合同，使检查能到达程序集依赖。</summary>
    public sealed class AuthoringDependencyFreezer :
        IConfigFreezer<AuthoringDependencyConfig, AuthoringDependencyConfigSchema>
    {
        /// <summary>检查用于依赖测试的节点配置，向调用方返回冻结结果及对应诊断。</summary>
        public bool TryFreeze(
            AuthoringDependencyConfig source,
            FrozenConfigWriter<AuthoringDependencyConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = source == null
                ? CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Config is required.")
                : CoCoDiagnostic.None;
            return source != null;
        }
    }
}
