using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoIdentityContractTestCases
    {
        [Test]
        public void TopologyAndTimelineIdsUseTwoUlongParts()
        {
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var graphId));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var layerId));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var stateId));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var transitionId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var timelineId));

            Assert.AreEqual(1UL, graphId.High);
            Assert.AreEqual(2UL, graphId.Low);
            Assert.AreEqual(3UL, layerId.High);
            Assert.AreEqual(4UL, layerId.Low);
            Assert.AreEqual(5UL, stateId.High);
            Assert.AreEqual(6UL, stateId.Low);
            Assert.AreEqual(7UL, transitionId.High);
            Assert.AreEqual(8UL, transitionId.Low);
            Assert.AreEqual(9UL, timelineId.High);
            Assert.AreEqual(10UL, timelineId.Low);
            Assert.AreEqual(32, graphId.ToString().Length);
            Assert.AreEqual(32, timelineId.ToString().Length);

            Assert.IsTrue(CoCoGraphId.TryParse(graphId.ToString(), out var parsedGraphId));
            Assert.IsTrue(CoCoLayerId.TryParse(layerId.ToString(), out var parsedLayerId));
            Assert.IsTrue(CoCoStateId.TryParse(stateId.ToString(), out var parsedStateId));
            Assert.IsTrue(CoCoTransitionId.TryParse(transitionId.ToString(), out var parsedTransitionId));
            Assert.IsTrue(CoCoTimelineId.TryParse(timelineId.ToString(), out var parsedTimelineId));

            Assert.AreEqual(graphId, parsedGraphId);
            Assert.AreEqual(layerId, parsedLayerId);
            Assert.AreEqual(stateId, parsedStateId);
            Assert.AreEqual(transitionId, parsedTransitionId);
            Assert.AreEqual(timelineId, parsedTimelineId);
        }

        [Test]
        public void StableIdsRejectAllZeroValues()
        {
            Assert.IsFalse(CoCoGraphId.TryCreate(0UL, 0UL, out var graphId));
            Assert.IsFalse(CoCoLayerId.TryCreate(0UL, 0UL, out var layerId));
            Assert.IsFalse(CoCoStateId.TryCreate(0UL, 0UL, out var stateId));
            Assert.IsFalse(CoCoTransitionId.TryCreate(0UL, 0UL, out var transitionId));
            Assert.IsFalse(CoCoTimelineId.TryCreate(0UL, 0UL, out var timelineId));

            Assert.IsFalse(graphId.IsValid);
            Assert.IsFalse(layerId.IsValid);
            Assert.IsFalse(stateId.IsValid);
            Assert.IsFalse(transitionId.IsValid);
            Assert.IsFalse(timelineId.IsValid);
        }

        [Test]
        public void RuntimeAndClockIdsRequireNonZeroValues()
        {
            Assert.IsFalse(CoCoGraphInstanceId.TryCreate(0UL, out var invalidInstanceId));
            Assert.IsFalse(CoCoActivationId.TryCreate(0UL, out var invalidActivationId));
            Assert.IsFalse(CoCoClockDomainId.TryCreate(0UL, out var invalidClockDomainId));

            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(1UL, out var instanceId));
            Assert.IsTrue(CoCoActivationId.TryCreate(2UL, out var activationId));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(3UL, out var clockDomainId));

            Assert.IsFalse(invalidInstanceId.IsValid);
            Assert.IsFalse(invalidActivationId.IsValid);
            Assert.IsFalse(invalidClockDomainId.IsValid);
            Assert.AreEqual(1UL, instanceId.Value);
            Assert.AreEqual(2UL, activationId.Value);
            Assert.AreEqual(3UL, clockDomainId.Value);
        }

        [Test]
        public void StableIdParsingRequiresExactlyThirtyTwoHexCharacters()
        {
            string[] invalidValues =
            {
                null,
                string.Empty,
                "1",
                new string('0', 31),
                new string('0', 33),
                new string('g', 32),
                new string('0', 32)
            };

            foreach (string invalidValue in invalidValues)
            {
                Assert.IsFalse(CoCoGraphId.TryParse(invalidValue, out _));
                Assert.IsFalse(CoCoTimelineId.TryParse(invalidValue, out _));
            }
        }

        [Test]
        public void StableIdsExposeSerializablePublicUlongFields()
        {
            AssertSerializableId(typeof(CoCoGraphId));
            AssertSerializableId(typeof(CoCoLayerId));
            AssertSerializableId(typeof(CoCoStateId));
            AssertSerializableId(typeof(CoCoTransitionId));
            AssertSerializableId(typeof(CoCoTimelineId));
            AssertSerializableId(typeof(CoCoGraphInstanceId), "Value");
            AssertSerializableId(typeof(CoCoActivationId), "Value");
            AssertSerializableId(typeof(CoCoClockDomainId), "Value");
        }

        [Test]
        public void EqualIdsHaveDeterministicEqualityAndHashCodes()
        {
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var leftGraph));
            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var rightGraph));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var leftLayer));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var rightLayer));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var leftState));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var rightState));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var leftTransition));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var rightTransition));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var leftTimeline));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var rightTimeline));
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(11UL, out var leftInstance));
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(11UL, out var rightInstance));
            Assert.IsTrue(CoCoActivationId.TryCreate(12UL, out var leftActivation));
            Assert.IsTrue(CoCoActivationId.TryCreate(12UL, out var rightActivation));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(13UL, out var leftClockDomain));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(13UL, out var rightClockDomain));

            AssertEqualAndHash(leftGraph, rightGraph);
            AssertEqualAndHash(leftLayer, rightLayer);
            AssertEqualAndHash(leftState, rightState);
            AssertEqualAndHash(leftTransition, rightTransition);
            AssertEqualAndHash(leftTimeline, rightTimeline);
            AssertEqualAndHash(leftInstance, rightInstance);
            AssertEqualAndHash(leftActivation, rightActivation);
            AssertEqualAndHash(leftClockDomain, rightClockDomain);
        }

        [Test]
        public void IdsSurviveUnityAssetSaveImportAndCopy()
        {
            string sourcePath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/CoCoFlowCoreContractsIdentityTest.asset");
            string copyPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/CoCoFlowCoreContractsIdentityCopyTest.asset");

            Assert.IsTrue(CoCoGraphId.TryCreate(1UL, 2UL, out var graphId));
            Assert.IsTrue(CoCoLayerId.TryCreate(3UL, 4UL, out var layerId));
            Assert.IsTrue(CoCoStateId.TryCreate(5UL, 6UL, out var stateId));
            Assert.IsTrue(CoCoTransitionId.TryCreate(7UL, 8UL, out var transitionId));
            Assert.IsTrue(CoCoTimelineId.TryCreate(9UL, 10UL, out var timelineId));
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(11UL, out var instanceId));
            Assert.IsTrue(CoCoActivationId.TryCreate(12UL, out var activationId));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(13UL, out var clockDomainId));

            try
            {
                var asset = ScriptableObject.CreateInstance<CoCoIdentityContractTests>();
                asset.GraphId = graphId;
                asset.LayerId = layerId;
                asset.StateId = stateId;
                asset.TransitionId = transitionId;
                asset.TimelineId = timelineId;
                asset.GraphInstanceId = instanceId;
                asset.ActivationId = activationId;
                asset.ClockDomainId = clockDomainId;

                AssetDatabase.CreateAsset(asset, sourcePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(
                    sourcePath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                Resources.UnloadAsset(asset);
                var reloaded = AssetDatabase.LoadAssetAtPath<CoCoIdentityContractTests>(sourcePath);
                AssertIdentityValues(
                    reloaded,
                    graphId,
                    layerId,
                    stateId,
                    transitionId,
                    timelineId,
                    instanceId,
                    activationId,
                    clockDomainId);

                Assert.IsTrue(AssetDatabase.CopyAsset(sourcePath, copyPath));
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(
                    copyPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var copied = AssetDatabase.LoadAssetAtPath<CoCoIdentityContractTests>(copyPath);
                AssertIdentityValues(
                    copied,
                    graphId,
                    layerId,
                    stateId,
                    transitionId,
                    timelineId,
                    instanceId,
                    activationId,
                    clockDomainId);
            }
            finally
            {
                AssetDatabase.DeleteAsset(copyPath);
                AssetDatabase.DeleteAsset(sourcePath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void AssertSerializableId(Type idType)
        {
            Assert.IsTrue(idType.IsDefined(typeof(SerializableAttribute), false), idType.FullName);
            Assert.IsFalse(idType.IsDefined(typeof(IsReadOnlyAttribute), false), idType.FullName);

            FieldInfo high = idType.GetField("High", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo low = idType.GetField("Low", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(high, idType.FullName);
            Assert.IsNotNull(low, idType.FullName);
            Assert.AreEqual(typeof(ulong), high.FieldType);
            Assert.AreEqual(typeof(ulong), low.FieldType);
            Assert.IsFalse(high.IsInitOnly);
            Assert.IsFalse(low.IsInitOnly);
        }

        private static void AssertSerializableId(Type idType, string fieldName)
        {
            Assert.IsTrue(idType.IsDefined(typeof(SerializableAttribute), false), idType.FullName);
            Assert.IsFalse(idType.IsDefined(typeof(IsReadOnlyAttribute), false), idType.FullName);

            FieldInfo value = idType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(value, idType.FullName);
            Assert.AreEqual(typeof(ulong), value.FieldType);
            Assert.IsFalse(value.IsInitOnly);
        }

        private static void AssertEqualAndHash<T>(T left, T right)
        {
            Assert.AreEqual(left, right);
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }

        private static void AssertIdentityValues(
            CoCoIdentityContractTests asset,
            CoCoGraphId graphId,
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoTransitionId transitionId,
            CoCoTimelineId timelineId,
            CoCoGraphInstanceId instanceId,
            CoCoActivationId activationId,
            CoCoClockDomainId clockDomainId)
        {
            Assert.IsNotNull(asset);
            Assert.AreEqual(graphId, asset.GraphId);
            Assert.AreEqual(layerId, asset.LayerId);
            Assert.AreEqual(stateId, asset.StateId);
            Assert.AreEqual(transitionId, asset.TransitionId);
            Assert.AreEqual(timelineId, asset.TimelineId);
            Assert.AreEqual(instanceId, asset.GraphInstanceId);
            Assert.AreEqual(activationId, asset.ActivationId);
            Assert.AreEqual(clockDomainId, asset.ClockDomainId);
        }
    }

    public sealed class CoCoIdentityContractTests : ScriptableObject
    {
        public CoCoGraphId GraphId;
        public CoCoLayerId LayerId;
        public CoCoStateId StateId;
        public CoCoTransitionId TransitionId;
        public CoCoTimelineId TimelineId;
        public CoCoGraphInstanceId GraphInstanceId;
        public CoCoActivationId ActivationId;
        public CoCoClockDomainId ClockDomainId;
    }
}
