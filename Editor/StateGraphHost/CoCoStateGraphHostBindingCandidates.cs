using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal static class CoCoStateGraphHostBindingCandidates
    {
        internal static void FindIntentSources(
            CoCoStateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Find(host, assigned, results, IsIntentSource);
        }

        internal static void FindEventAdapters(
            CoCoStateGraphHost host,
            Type eventType,
            Type intentType,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Type expected = eventType == null || intentType == null
                ? null
                : typeof(ICoCoEventToIntentAdapter<,>).MakeGenericType(
                    eventType,
                    intentType);
            Find(
                host,
                assigned,
                results,
                component => expected != null &&
                             expected.IsAssignableFrom(component.GetType()));
        }

        internal static bool IsEventAdapter(
            MonoBehaviour component,
            Type eventType,
            Type intentType)
        {
            if (component == null || eventType == null || intentType == null)
            {
                return false;
            }

            Type expected = typeof(ICoCoEventToIntentAdapter<,>).MakeGenericType(
                eventType,
                intentType);
            return expected.IsAssignableFrom(component.GetType());
        }

        internal static bool IsIntentSource(MonoBehaviour component)
        {
            if (component == null)
            {
                return false;
            }

            Type[] interfaces = component.GetType().GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type contract = interfaces[index];
                if (contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() == typeof(ICoCoIntentFrameSource<>))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Find(
            CoCoStateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results,
            Func<MonoBehaviour, bool> matches)
        {
            results.Clear();
            if (host == null || matches == null)
            {
                return;
            }

            MonoBehaviour[] components = host.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component == null ||
                    !CoCoStateGraphHostBoundary.Contains(host, component) ||
                    Contains(assigned, component) ||
                    !matches(component))
                {
                    continue;
                }

                results.Add(component);
            }
        }

        private static bool Contains(
            IReadOnlyList<MonoBehaviour> assigned,
            MonoBehaviour candidate)
        {
            if (assigned == null)
            {
                return false;
            }

            for (int index = 0; index < assigned.Count; index++)
            {
                if (ReferenceEquals(assigned[index], candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
