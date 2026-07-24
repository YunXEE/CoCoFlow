using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Tests
{
    public sealed class PoolingContractTests
    {
        [Test]
        public void PoolIdTrimsOnceAndUsesOrdinalValueEquality()
        {
            Assert.That(PoolId.TryCreate("  effects.muzzle  ", out PoolId id), Is.True);
            Assert.That(PoolId.TryCreate("effects.muzzle", out PoolId same), Is.True);
            Assert.That(PoolId.TryCreate("Effects.Muzzle", out PoolId differentCase), Is.True);

            Assert.That(id.Value, Is.EqualTo("effects.muzzle"));
            Assert.That(id, Is.EqualTo(same));
            Assert.That(id.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(id, Is.Not.EqualTo(differentCase));
            Assert.That(PoolId.TryCreate(" ", out PoolId invalid), Is.False);
            Assert.That(invalid.IsValid, Is.False);
        }

        [Test]
        public void ProfileAllowsZeroRetentionOnlyWithZeroPrewarm()
        {
            var prefab = new GameObject("Pooling Contract Prefab");
            try
            {
                PoolId.TryCreate("tests.zero-retained", out PoolId poolId);
                ContentId.TryCreate("tests.zero-retained.prefab", out ContentId contentId);
                Assert.That(
                    ContentReference.TryCreateDirectPrefabSource(
                        contentId,
                        prefab,
                        out ContentReference source),
                    Is.True);

                Assert.That(
                    PoolProfile.TryCreate(poolId, source, 0, 0, out PoolProfile zero),
                    Is.True);
                Assert.That(zero.IsValid, Is.True);
                Assert.That(zero.PrewarmCount, Is.Zero);
                Assert.That(zero.MaxRetained, Is.Zero);

                Assert.That(
                    PoolProfile.TryCreate(poolId, source, 1, 0, out _),
                    Is.False);
                Assert.That(
                    PoolProfile.TryCreate(poolId, source, -1, 1, out _),
                    Is.False);
                Assert.That(
                    PoolProfile.TryCreate(poolId, source, 0, -1, out _),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ProfileAcceptsOnlyPrefabSourceReferences()
        {
            var prefab = new GameObject("Pooling Profile Prefab");
            var asset = ScriptableObject.CreateInstance<PoolingContractAsset>();
            try
            {
                PoolId.TryCreate("tests.profile-kind", out PoolId poolId);
                ContentId.TryCreate("tests.profile-kind.prefab", out ContentId prefabId);
                ContentId.TryCreate("tests.profile-kind.asset", out ContentId assetId);
                ContentReference.TryCreateDirectPrefabSource(
                    prefabId,
                    prefab,
                    out ContentReference prefabSource);
                ContentReference.TryCreateDirectAsset(
                    assetId,
                    asset,
                    out ContentReference assetSource);

                Assert.That(
                    PoolProfile.TryCreate(poolId, prefabSource, 2, 4, out PoolProfile profile),
                    Is.True);
                Assert.That(profile.PrefabSource.Kind, Is.EqualTo(ContentKind.PrefabSource));
                Assert.That(
                    PoolProfile.TryCreate(poolId, assetSource, 0, 1, out _),
                    Is.False);
                Assert.That(
                    PoolProfile.TryCreate(default, prefabSource, 0, 1, out _),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void HandleIsReadonlyGenerationTokenAndOnlyDisposeReturnsImplicitly()
        {
            Type handleType = typeof(PooledHandle);

            Assert.That(handleType.IsValueType, Is.True);
            Assert.That(
                handleType.IsDefined(typeof(IsReadOnlyAttribute), false),
                Is.True);
            Assert.That(typeof(IDisposable).IsAssignableFrom(handleType), Is.True);
            Assert.That(
                handleType.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
                Is.Empty);
            Assert.That(
                handleType.GetFields(BindingFlags.Public | BindingFlags.Instance),
                Is.Empty);

            AssertGetterOnly(handleType, nameof(PooledHandle.PoolId), typeof(PoolId));
            AssertGetterOnly(handleType, nameof(PooledHandle.ScopeSequence), typeof(long));
            AssertGetterOnly(handleType, nameof(PooledHandle.InstanceSequence), typeof(long));
            AssertGetterOnly(handleType, nameof(PooledHandle.Generation), typeof(uint));
            Assert.That(handleType.GetMethod(nameof(PooledHandle.TryActivate)), Is.Not.Null);
            Assert.That(handleType.GetMethod(nameof(PooledHandle.TryReturn)), Is.Not.Null);
            Assert.That(handleType.GetMethod(nameof(PooledHandle.TryGetInstance)), Is.Not.Null);
        }

        [Test]
        public void PoolableContractIsSynchronousAndFailureAware()
        {
            MethodInfo[] methods = typeof(IPoolable)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(method => method.Name)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "TryOnRent", "TryOnReturn" },
                methods.Select(method => method.Name).ToArray());
            foreach (MethodInfo method in methods)
            {
                Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
                Assert.That(method.GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);
                ParameterInfo[] parameters = method.GetParameters();
                Assert.That(parameters, Has.Length.EqualTo(2));
                Assert.That(parameters[0].ParameterType.IsByRef, Is.True);
                Assert.That(parameters[1].IsOut, Is.True);
                Assert.That(
                    parameters[1].ParameterType,
                    Is.EqualTo(typeof(CoCoDiagnostic).MakeByRefType()));
            }
        }

        [Test]
        public void PublicPoolingSurfaceDoesNotExposeUnityObjectPool()
        {
            Assembly assembly = typeof(PoolId).Assembly;
            Type offendingType = assembly
                .GetExportedTypes()
                .SelectMany(type => PublicSurfaceTypes(type).Append(type))
                .FirstOrDefault(type =>
                    string.Equals(
                        Unwrap(type).Namespace,
                        "UnityEngine.Pool",
                        StringComparison.Ordinal));

            Assert.That(
                offendingType,
                Is.Null,
                offendingType == null
                    ? string.Empty
                    : "Public Pooling API exposed " + offendingType.FullName);
        }

        [Test]
        public void DiagnosticSnapshotsRetainNoUnityObjectLeaseOrHandleAuthority()
        {
            Type[] snapshotTypes =
            {
                typeof(PoolRuntimeSnapshot),
                typeof(PoolScopeSnapshot),
                typeof(PoolEntrySnapshot),
                typeof(PoolDiagnosticRecord)
            };

            foreach (Type snapshotType in snapshotTypes)
            {
                foreach (Type surfaceType in PublicSurfaceTypes(snapshotType))
                {
                    Type valueType = Unwrap(surfaceType);
                    Assert.That(
                        typeof(UnityEngine.Object).IsAssignableFrom(valueType),
                        Is.False,
                        snapshotType.FullName + " exposes " + valueType.FullName);
                    Assert.That(
                        typeof(ContentLease).IsAssignableFrom(valueType),
                        Is.False,
                        snapshotType.FullName + " exposes lease authority.");
                    Assert.That(
                        valueType,
                        Is.Not.EqualTo(typeof(PooledHandle)),
                        snapshotType.FullName + " exposes return authority.");
                }
            }
        }

        private static void AssertGetterOnly(
            Type type,
            string propertyName,
            Type propertyType)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(propertyType));
            Assert.That(property.GetGetMethod(false), Is.Not.Null);
            Assert.That(property.GetSetMethod(true), Is.Null);
        }

        private static Type Unwrap(Type type)
        {
            Type valueType = type;
            while (valueType.HasElementType)
            {
                valueType = valueType.GetElementType();
            }

            if (valueType != null &&
                valueType.IsGenericType &&
                valueType.GetGenericArguments().Length == 1)
            {
                return Unwrap(valueType.GetGenericArguments()[0]);
            }

            return valueType;
        }

        private static System.Collections.Generic.IEnumerable<Type> PublicSurfaceTypes(
            Type type)
        {
            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                yield return field.FieldType;
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                yield return property.PropertyType;
            }

            foreach (EventInfo eventInfo in type.GetEvents(flags))
            {
                yield return eventInfo.EventHandlerType;
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                yield return method.ReturnType;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }
        }

        private sealed class PoolingContractAsset : ScriptableObject
        {
        }
    }
}
