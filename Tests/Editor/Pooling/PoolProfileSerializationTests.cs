using CoCoFlow.Runtime.Content;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling.Tests
{
    public sealed class PoolProfileSerializationTests
    {
        [Test]
        public void JsonRoundTripPreservesSerializableProfileValue()
        {
            var prefab = new GameObject("Serializable Pool Profile Prefab");
            try
            {
                PoolId.TryCreate("tests.serializable-profile", out PoolId poolId);
                ContentId.TryCreate(
                    "tests.serializable-profile.prefab",
                    out ContentId contentId);
                ContentReference.TryCreateDirectPrefabSource(
                    contentId,
                    prefab,
                    out ContentReference source);
                Assert.That(
                    PoolProfile.TryCreate(poolId, source, 3, 8, out PoolProfile profile),
                    Is.True);

                string json = JsonUtility.ToJson(profile);
                PoolProfile restored = JsonUtility.FromJson<PoolProfile>(json);

                Assert.That(restored, Is.EqualTo(profile));
                Assert.That(restored.Id, Is.EqualTo(poolId));
                Assert.That(restored.PrefabSource, Is.EqualTo(source));
                Assert.That(restored.PrewarmCount, Is.EqualTo(3));
                Assert.That(restored.MaxRetained, Is.EqualTo(8));
                Assert.That(restored.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
