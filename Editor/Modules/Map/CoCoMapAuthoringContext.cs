using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    /// <summary>
    /// Explicit Editor-only provider seam used by authoring and Player build validation.
    /// Project Editor code may assign these delegates during domain initialization.
    /// The global catalog must contain the union of registrations used by every
    /// binding discovered by build validation.
    /// </summary>
    public static class CoCoMapEditorCatalogProvider
    {
        public static Func<RegionParticipantCatalog> CatalogProvider { get; set; }
        public static Func<IRegionAddressableSceneResolver>
            AddressableSceneResolverProvider { get; set; }
    }

    internal static class CoCoMapAuthoringContext
    {
        internal static bool TryCompile(
            CoCoRegionBinding binding,
            out RegionCompileResult result,
            out string failure)
        {
            result = null;
            if (binding == null)
            {
                failure = "A Region Binding is required.";
                return false;
            }

            if (!TryResolveForBinding(
                    binding,
                    out RegionParticipantCatalog catalog,
                    out IRegionAddressableSceneResolver resolver,
                    out failure))
            {
                return false;
            }

            result = new RegionBindingCompiler().Compile(
                binding,
                catalog,
                resolver);
            failure = string.Empty;
            return true;
        }

        internal static bool TryResolveGlobal(
            out RegionParticipantCatalog catalog,
            out IRegionAddressableSceneResolver resolver,
            out string failure)
        {
            if (TryResolveRegistered(
                    out catalog,
                    out resolver,
                    out failure))
            {
                return true;
            }

            catalog = null;
            resolver = null;
            failure =
                "Player build validation requires an explicit " +
                "CoCoMapEditorCatalogProvider.CatalogProvider that contains " +
                "the union of registrations used by all bindings discovered " +
                "by build validation. " + failure;
            return false;
        }

        internal static bool TryResolveForBinding(
            CoCoRegionBinding binding,
            out RegionParticipantCatalog catalog,
            out IRegionAddressableSceneResolver resolver,
            out string failure)
        {
            CoCoMapHost[] hosts =
                Resources.FindObjectsOfTypeAll<CoCoMapHost>();
            Array.Sort(
                hosts,
                (left, right) => string.CompareOrdinal(
                    GetStableHostPath(left),
                    GetStableHostPath(right)));
            for (int index = 0; index < hosts.Length; index++)
            {
                CoCoMapHost host = hosts[index];
                if (!IsLoadedSceneObject(host) ||
                    !HostContainsBinding(host, binding))
                {
                    continue;
                }

                if (TryResolveFromHost(
                        host,
                        out catalog,
                        out resolver,
                        out failure))
                {
                    return true;
                }
            }

            return TryResolveRegistered(
                out catalog,
                out resolver,
                out failure);
        }

        internal static bool HasMissingManagedReferences(
            UnityEngine.Object asset)
        {
            return asset != null &&
                   SerializationUtility.HasManagedReferencesWithMissingTypes(asset);
        }

        private static bool TryResolveRegistered(
            out RegionParticipantCatalog catalog,
            out IRegionAddressableSceneResolver resolver,
            out string failure)
        {
            catalog = null;
            resolver = null;
            Func<RegionParticipantCatalog> catalogProvider =
                CoCoMapEditorCatalogProvider.CatalogProvider;
            if (catalogProvider == null)
            {
                failure =
                    "No explicit Editor Region catalog provider is registered.";
                return false;
            }

            try
            {
                catalog = catalogProvider();
            }
            catch (Exception exception)
            {
                failure =
                    "The Editor Region catalog provider threw: " +
                    exception.Message;
                return false;
            }

            if (catalog == null)
            {
                failure =
                    "The Editor Region catalog provider returned null.";
                return false;
            }

            if (!catalog.IsSealed)
            {
                catalog.Seal();
            }

            Func<IRegionAddressableSceneResolver> resolverProvider =
                CoCoMapEditorCatalogProvider.AddressableSceneResolverProvider;
            if (resolverProvider != null)
            {
                try
                {
                    resolver = resolverProvider();
                }
                catch (Exception exception)
                {
                    failure =
                        "The Editor Addressable Scene resolver provider threw: " +
                        exception.Message;
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryResolveFromHost(
            CoCoMapHost host,
            out RegionParticipantCatalog catalog,
            out IRegionAddressableSceneResolver resolver,
            out string failure)
        {
            catalog = null;
            resolver = null;
            if (host == null)
            {
                failure = "The Region Host is missing.";
                return false;
            }

            var serializedHost = new SerializedObject(host);
            SerializedProperty catalogProperty =
                serializedHost.FindProperty("catalogProviderComponent");
            MonoBehaviour catalogComponent =
                catalogProperty?.objectReferenceValue as MonoBehaviour;
            if (!(catalogComponent is
                    IRegionParticipantCatalogProvider catalogProvider))
            {
                failure =
                    "CoCoMapHost '" + GetStableHostPath(host) +
                    "' has no valid explicit catalog provider.";
                return false;
            }

            CoCoDiagnostic diagnostic;
            try
            {
                if (!catalogProvider.TryGetCatalog(
                        out catalog,
                        out diagnostic))
                {
                    failure = diagnostic.IsNone
                        ? "The Region catalog provider rejected the request."
                        : diagnostic.Message;
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure =
                    "The Region catalog provider threw: " +
                    exception.Message;
                return false;
            }

            if (catalog == null)
            {
                failure =
                    "The Region catalog provider returned null.";
                return false;
            }

            if (!catalog.IsSealed)
            {
                catalog.Seal();
            }

            SerializedProperty resolverProperty =
                serializedHost.FindProperty(
                    "addressableSceneResolverComponent");
            MonoBehaviour resolverComponent =
                resolverProperty?.objectReferenceValue as MonoBehaviour;
            if (resolverComponent != null &&
                !(resolverComponent is IRegionAddressableSceneResolver))
            {
                failure =
                    "CoCoMapHost '" + GetStableHostPath(host) +
                    "' has an invalid Addressable Scene resolver.";
                return false;
            }

            resolver = resolverComponent as IRegionAddressableSceneResolver;
            failure = string.Empty;
            return true;
        }

        private static bool HostContainsBinding(
            CoCoMapHost host,
            CoCoRegionBinding binding)
        {
            var serializedHost = new SerializedObject(host);
            SerializedProperty bindings =
                serializedHost.FindProperty("bootstrapBindings");
            if (bindings == null || !bindings.isArray) return false;

            for (int index = 0; index < bindings.arraySize; index++)
            {
                if (bindings.GetArrayElementAtIndex(index)
                        .objectReferenceValue == binding)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLoadedSceneObject(CoCoMapHost host)
        {
            return host != null &&
                   host.gameObject.scene.IsValid() &&
                   host.gameObject.scene.isLoaded &&
                   !EditorUtility.IsPersistent(host);
        }

        private static string GetStableHostPath(CoCoMapHost host)
        {
            if (host == null) return string.Empty;
            return host.gameObject.scene.path + "/" +
                   GetTransformPath(host.transform);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
