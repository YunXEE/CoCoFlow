using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将一个 Intent 注册到图目录并为实例创建来源绑定。</summary>
    internal interface IStandardIntentRegistration
    {
        Type IntentType { get; }
        IntentId IntentId { get; }
        bool RegisterCatalog(GraphDescriptorCatalogBuilder builder);
        bool TryRegisterHost(HostBindingBuilder builder,
            out IStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic);
    }

    /// <summary>匹配场景中的输入来源并接入已登记的类型化槽。</summary>
    internal interface IStandardIntentSourceBinding
    {
        bool Accepts(MonoBehaviour component);
        bool TryBind(HostBindingBuilder builder,
            int hostSourceIndex, out CoCoDiagnostic diagnostic);
    }

    /// <summary>声明项目 Intent 的归并器、单步贡献容量和语义指纹。每个 Host 启动时创建独立归并器。</summary>
    /// <typeparam name="TIntent">参与归并的项目输入类型。</typeparam>
    /// <typeparam name="TReducer">接收同类输入贡献的归并器类型。</typeparam>
    /// <typeparam name="TFactory">为单个实例创建归并器的工厂类型。</typeparam>
    public class HostIntentRegistration<TIntent, TReducer, TFactory> :
        IStandardIntentRegistration
        where TReducer : unmanaged, IIntentReducer<TIntent>
        where TFactory : IIntentReducerFactory<TIntent, TReducer>, new()
    {
        #region Public API

        /// <summary>归并器消费的载荷类型。</summary>
        public Type IntentType => typeof(TIntent);

        /// <summary>供图与场景绑定使用的稳定输入标识。</summary>
        public IntentId IntentId { get; }

        /// <summary>在项目注册发现前声明一个 Intent 的身份、贡献上限与指纹。</summary>
        /// <param name="intentId">项目为此输入分配的稳定标识。</param>
        /// <param name="maxContributions">归并器单步能接受的贡献上限。</param>
        /// <param name="semanticFingerprint">注册者提供的稳定语义指纹。</param>
        public HostIntentRegistration(IntentId intentId,
            int maxContributions, ulong semanticFingerprint)
        {
            IntentId = intentId;
            _maxContributions = maxContributions;
            _fingerprint = semanticFingerprint;
        }

        #endregion

        #region Internal Logic

        private readonly int _maxContributions;
        private readonly ulong _fingerprint;

        bool IStandardIntentRegistration.RegisterCatalog(GraphDescriptorCatalogBuilder builder)
        {
            if (!builder.TryRegisterIntent(IntentId, _maxContributions,
                    new IntentReducerFactoryToken<TIntent, TReducer, TFactory>(_fingerprint), out var diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
            return true;
        }

        bool IStandardIntentRegistration.TryRegisterHost(HostBindingBuilder builder,
            out IStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic)
        {
            binding = null;
            if (!builder.TryRegisterIntent<TIntent, TReducer, TFactory>(IntentId,
                    new TFactory(), _fingerprint, out var handle, out diagnostic)) return false;
            binding = new SourceBinding(handle);
            return true;
        }

        /// <summary>把一个场景输入组件接入持有的类型化 Intent 槽。</summary>
        private sealed class SourceBinding : IStandardIntentSourceBinding
        {
            private readonly IntentHandle<TIntent> _handle;
            public SourceBinding(IntentHandle<TIntent> handle) { _handle = handle; }
            public bool Accepts(MonoBehaviour component) =>
                component != null && component is IIntentFrameSource<TIntent>;

            public bool TryBind(HostBindingBuilder builder,
                int hostSourceIndex, out CoCoDiagnostic diagnostic)
            {
                if (!IntentSourceRequirement<TIntent>.TryCreate(_handle, 1, out var source))
                {
                    diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry,
                        CoCoDiagnosticCode.MissingIntentReducer, "Intent source requirement is invalid.");
                    return false;
                }
                return builder.TryBindIntentSource(hostSourceIndex, source, out diagnostic);
            }
        }

        #endregion
    }

    /// <summary>为单来源 Intent 创建默认的透传归并注册。</summary>
    internal static class StandardSingleIntent
    {
        private static readonly MethodInfo CreateRegistration = typeof(StandardSingleIntent)
            .GetMethod(nameof(CreateTyped), BindingFlags.Static | BindingFlags.NonPublic);

        internal static IStandardIntentRegistration For(Type payload) =>
            (IStandardIntentRegistration)CreateRegistration.MakeGenericMethod(payload).Invoke(null, null);

        private static IStandardIntentRegistration CreateTyped<T>()
        {
            IntentId.TryCreate(0x434F434F494E544EUL, HostBindingIds.Hash(typeof(T)), out var id);
            return new HostIntentRegistration<T, IntentPassThrough<T>, IntentPassThroughFactory<T>>(id, 1, 1);
        }

    }
}
