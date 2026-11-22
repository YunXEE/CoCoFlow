using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Editor.Core.Tests
{
    public sealed class CoCoContextProviderDrawerTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        private GameObject hierarchyRoot;
        private GameObject unrelatedRoot;
        private ContextProviderDrawerTestProvider selfProvider;
        private ContextProviderDrawerTestProvider parentProvider;
        private ContextProviderDrawerTestProvider childProvider;
        private ContextProviderDrawerTestProvider siblingProvider;
        private ContextProviderDrawerTestProvider unrelatedProvider;
        private ContextProviderDrawerTestConsumer sourceConsumer;
        private ContextProviderDrawerTestConsumer siblingConsumer;

        [SetUp]
        public void SetUp()
        {
            hierarchyRoot = new GameObject("Hierarchy Root");
            var parent = CreateChild(hierarchyRoot.transform, "Parent");
            var source = CreateChild(parent, "Source");
            var child = CreateChild(source, "Child");
            var sibling = CreateChild(parent, "Sibling");
            unrelatedRoot = new GameObject("Unrelated Root");

            selfProvider = source.gameObject.AddComponent<ContextProviderDrawerTestProvider>();
            parentProvider = parent.gameObject.AddComponent<ContextProviderDrawerTestProvider>();
            childProvider = child.gameObject.AddComponent<ContextProviderDrawerTestProvider>();
            siblingProvider = sibling.gameObject.AddComponent<ContextProviderDrawerTestProvider>();
            unrelatedProvider = unrelatedRoot.AddComponent<ContextProviderDrawerTestProvider>();
            sourceConsumer = source.gameObject.AddComponent<ContextProviderDrawerTestConsumer>();
            siblingConsumer = sibling.gameObject.AddComponent<ContextProviderDrawerTestConsumer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(hierarchyRoot);
            Object.DestroyImmediate(unrelatedRoot);
        }

        [Test]
        public void CandidateCollectionIncludesOnlySelfAncestorsAndDescendants()
        {
            var candidates = CollectCandidates(
                sourceConsumer.transform,
                typeof(ContextProviderDrawerTestContext));

            CollectionAssert.AreEquivalent(
                new[] { selfProvider, parentProvider, childProvider },
                candidates);
            CollectionAssert.DoesNotContain(candidates, siblingProvider);
            CollectionAssert.DoesNotContain(candidates, unrelatedProvider);
        }

        [Test]
        public void ManualSelectionScopeAllowsSelfParentAndChild()
        {
            var targets = new Object[] { sourceConsumer };

            Assert.IsTrue(IsManualSelectionInScope(targets, selfProvider));
            Assert.IsTrue(IsManualSelectionInScope(targets, parentProvider));
            Assert.IsTrue(IsManualSelectionInScope(targets, childProvider));
            Assert.IsFalse(IsManualSelectionInScope(targets, siblingProvider));
            Assert.IsFalse(IsManualSelectionInScope(targets, unrelatedProvider));
        }

        [Test]
        public void ManualSelectionScopeRequiresProviderToBeValidForEveryTarget()
        {
            var targets = new Object[] { sourceConsumer, siblingConsumer };

            Assert.IsTrue(IsManualSelectionInScope(targets, parentProvider));
            Assert.IsFalse(IsManualSelectionInScope(targets, selfProvider));
            Assert.IsFalse(IsManualSelectionInScope(targets, siblingProvider));
            Assert.IsFalse(IsManualSelectionInScope(targets, unrelatedProvider));
        }

        private static List<MonoBehaviour> CollectCandidates(
            Transform sourceTransform,
            System.Type requiredContextType)
        {
            var method = typeof(CoCoContextProviderDrawer).GetMethod(
                "CollectContextProviderCandidates",
                StaticPrivate);
            Assert.IsNotNull(method);

            var result = method.Invoke(null, new object[] { sourceTransform, requiredContextType });
            Assert.IsInstanceOf<IEnumerable>(result);

            var providers = new List<MonoBehaviour>();
            foreach (var candidate in (IEnumerable)result)
            {
                var providerProperty = candidate.GetType().GetProperty("Provider");
                Assert.IsNotNull(providerProperty);
                providers.Add((MonoBehaviour)providerProperty.GetValue(candidate));
            }

            return providers;
        }

        private static bool IsManualSelectionInScope(
            Object[] targetObjects,
            MonoBehaviour provider)
        {
            var method = typeof(CoCoContextProviderDrawer).GetMethod(
                "IsContextProviderInScopeForAllTargets",
                StaticPrivate);
            Assert.IsNotNull(method);

            return (bool)method.Invoke(null, new object[] { targetObjects, provider });
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
    }

    public sealed class ContextProviderDrawerTestContext : ICoCoContext { }

    public sealed class ContextProviderDrawerTestProvider :
        MonoBehaviour,
        ICoCoContextProvider<ContextProviderDrawerTestContext>
    {
        public ContextProviderDrawerTestContext Context { get; } =
            new ContextProviderDrawerTestContext();
    }

    public sealed class ContextProviderDrawerTestConsumer : MonoBehaviour { }
}
