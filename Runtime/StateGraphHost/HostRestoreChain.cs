using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 在开始恢复前检查完整组件链，防止跨宿主写入或重复应用同一组件。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>提供按顺序接收恢复值的下游场景组件。</summary>
    internal interface IHostRestoreDecorator
    {
        MonoBehaviour DownstreamRestoreBinding { get; }
    }

    /// <summary>检查恢复链中每个组件的归属、能力与循环引用。</summary>
    internal static class HostRestoreChain
    {
        internal static bool TryValidate(
            HostComponentBoundary boundary,
            MonoBehaviour root,
            out CoCoDiagnostic diagnostic)
        {
            if (!boundary.HasOwner ||
                root == null ||
                !(root is IHostRestoreBinding) ||
                !boundary.Contains(root))
            {
                diagnostic = InvalidChain(
                    "Temporal Restore Binding chain requires one live root inside the Host boundary.");
                return false;
            }

            var visited = new List<MonoBehaviour>();
            MonoBehaviour current = root;
            while (!ReferenceEquals(current, null))
            {
                if (current == null || current is IOperator ||
                    !(current is IHostRestoreBinding) ||
                    !boundary.Contains(current))
                {
                    diagnostic = InvalidChain(
                        "Every Temporal decorator target must remain a live Restore Binding inside the same Host boundary.");
                    return false;
                }

                for (int index = 0; index < visited.Count; index++)
                {
                    if (ReferenceEquals(visited[index], current))
                    {
                        diagnostic = InvalidChain(
                            "Temporal Restore Binding decorator chain contains a direct or indirect cycle.");
                        return false;
                    }
                }

                visited.Add(current);
                if (!(current is IHostRestoreDecorator decorator))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                current = decorator.DownstreamRestoreBinding;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic InvalidChain(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidActorBinding,
                message);
    }
}
