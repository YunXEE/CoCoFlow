using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Content.Tests
{
    public sealed class ContentContractTests
    {
        [Test]
        public void ContentVocabularyIsExactAndBackendIdsAreStrongTypes()
        {
            CollectionAssert.AreEqual(
                new[] { "Asset", "PrefabSource", "AdditiveScene" },
                Enum.GetNames(typeof(ContentKind)));
            CollectionAssert.AreEqual(
                new[] { "Direct", "Addressables" },
                Enum.GetNames(typeof(ContentSourceKind)));
            Assert.IsTrue(typeof(ContentLease).IsClass);
            Assert.IsTrue(typeof(ContentScope).IsClass);
            Assert.AreNotEqual(typeof(string), typeof(ContentBackendId));
            Assert.IsTrue(ContentBackendId.TryCreate("backend.test", out ContentBackendId id));
            Assert.AreEqual("backend.test", id.Value);
        }

        [Test]
        public void ContentReferenceFactoriesProduceCanonicalSerializableReferences()
        {
            Assert.IsTrue(typeof(ContentReference).IsDefined(typeof(SerializableAttribute), false));
            Assert.IsTrue(ContentId.TryCreate("content.test", out ContentId id));
            var asset = ScriptableObject.CreateInstance<ContentContractAsset>();
            var prefab = new GameObject("Content Contract Prefab Source");
            try
            {
                Assert.IsTrue(ContentReference.TryCreateDirectAsset(
                    id,
                    asset,
                    out ContentReference directAsset));
                Assert.AreEqual(ContentKind.Asset, directAsset.Kind);
                Assert.AreEqual(ContentSourceKind.Direct, directAsset.SourceKind);
                Assert.AreSame(asset, directAsset.DirectObject);
                Assert.AreEqual(string.Empty, directAsset.Location);
                Assert.IsTrue(directAsset.IsValid);

                Assert.IsTrue(ContentReference.TryCreateDirectPrefabSource(
                    id,
                    prefab,
                    out ContentReference directPrefab));
                Assert.AreEqual(ContentKind.PrefabSource, directPrefab.Kind);
                Assert.AreSame(prefab, directPrefab.DirectObject);
                Assert.AreEqual(string.Empty, directPrefab.Location);

                Assert.IsTrue(ContentReference.TryCreateDirectAdditiveScene(
                    id,
                    "Assets/Scenes/Test.unity",
                    out ContentReference directScene));
                Assert.AreEqual(ContentKind.AdditiveScene, directScene.Kind);
                Assert.IsNull(directScene.DirectObject);
                Assert.AreEqual("Assets/Scenes/Test.unity", directScene.Location);

                Assert.IsTrue(ContentReference.TryCreateAddressablePrefabSource(
                    id,
                    "ui/test-panel",
                    out ContentReference addressable));
                Assert.AreEqual(ContentSourceKind.Addressables, addressable.SourceKind);
                Assert.AreEqual(ContentKind.PrefabSource, addressable.Kind);
                Assert.IsNull(addressable.DirectObject);
                Assert.AreEqual("ui/test-panel", addressable.Location);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void SerializedIdentitiesRejectLeadingOrTrailingWhitespace()
        {
            ContentId paddedContentId = JsonUtility.FromJson<ContentId>(
                "{\"value\":\" content.test \"}");
            ContentOwnerId paddedOwnerId = JsonUtility.FromJson<ContentOwnerId>(
                "{\"value\":\" owner.test \"}");

            Assert.IsFalse(paddedContentId.IsValid);
            Assert.IsFalse(paddedOwnerId.IsValid);
            Assert.IsTrue(ContentId.TryCreate(" content.test ", out ContentId contentId));
            Assert.IsTrue(ContentOwnerId.TryCreate(" owner.test ", out ContentOwnerId ownerId));
            Assert.AreEqual("content.test", contentId.Value);
            Assert.AreEqual("owner.test", ownerId.Value);
        }

        [Test]
        public void PublicContentSurfaceExposesNoAddressablesHandleTypes()
        {
            Assembly assembly = typeof(ContentRuntime).Assembly;
            foreach (Type type in assembly.GetExportedTypes())
            {
                AssertNoAddressablesHandle(type.BaseType, type.FullName + " base");
                foreach (PropertyInfo property in type.GetProperties(
                             BindingFlags.Public |
                             BindingFlags.Instance |
                             BindingFlags.Static |
                             BindingFlags.DeclaredOnly))
                {
                    AssertNoAddressablesHandle(property.PropertyType, type.FullName + "." + property.Name);
                }

                foreach (MethodInfo method in type.GetMethods(
                             BindingFlags.Public |
                             BindingFlags.Instance |
                             BindingFlags.Static |
                             BindingFlags.DeclaredOnly))
                {
                    AssertNoAddressablesHandle(method.ReturnType, type.FullName + "." + method.Name);
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        AssertNoAddressablesHandle(
                            parameter.ParameterType,
                            type.FullName + "." + method.Name + " parameter " + parameter.Name);
                    }
                }
            }

            string[] references = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            CollectionAssert.DoesNotContain(references, "Unity.Addressables");
            CollectionAssert.DoesNotContain(references, "Unity.ResourceManager");
        }

        private static void AssertNoAddressablesHandle(Type type, string location)
        {
            if (type == null) return;

            string fullName = type.IsGenericType
                ? type.GetGenericTypeDefinition().FullName
                : type.FullName;
            Assert.IsFalse(
                fullName != null &&
                fullName.StartsWith(
                    "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle",
                    StringComparison.Ordinal),
                location + " exposes " + fullName + ".");

            if (!type.IsGenericType) return;
            foreach (Type argument in type.GetGenericArguments())
            {
                AssertNoAddressablesHandle(argument, location);
            }
        }

        private sealed class ContentContractAsset : ScriptableObject
        {
        }
    }
}
