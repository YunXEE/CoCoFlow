using System;
using UnityEngine;

// 场景接线用组件身份与宿主类型检查归属，包括未激活的父物体和嵌套宿主。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>按最近的宿主组件界定一台 Host 管辖的层级。</summary>
    internal readonly struct HostComponentBoundary
    {
        internal Component Owner { get; }
        private readonly Func<Transform, Component> _findHost;

        private HostComponentBoundary(Component owner, Func<Transform, Component> findHost)
        {
            Owner = owner;
            _findHost = findHost;
        }

        internal static HostComponentBoundary For<THost>(THost owner) where THost : Component =>
            new HostComponentBoundary(owner, parent => parent.GetComponent<THost>());

        internal bool HasOwner => Owner != null;

        internal bool Contains(MonoBehaviour component)
        {
            if (Owner == null || component == null) return false;

            for (Transform parent = component.transform; parent != null; parent = parent.parent)
            {
                Component nearest = _findHost(parent);
                if (nearest != null) return ReferenceEquals(nearest, Owner);
            }

            return false;
        }
    }
}
