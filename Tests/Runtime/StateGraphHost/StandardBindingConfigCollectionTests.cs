using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed partial class StandardBindingPlayModeTests
    {
        [TearDown]
        public void ClearCollectionProbes() => ConfigCollectionProbeLogic.Seen.Clear();

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void ConfigCollectionsRoundTripAndReplaceWholeOverrides(int count)
        {
            var windows = Enumerable.Range(0, count).Select(i => new ConfigCollectionWindow
            {
                startSeconds = i + 0.25f, endSeconds = i + 0.75d,
                isEnabled = true, mode = ConfigCollectionMode.Second, timing = new ConfigCollectionTiming { Weight = i + 0.5m }
            }).Reverse().ToArray();
            var config = new CoCoFieldConfig();
            config.Set("Numbers", Enumerable.Range(0, count).Select(i => (float)i).Reverse().ToArray());
            config.Set("Samples", new List<float>(Enumerable.Range(0, count).Select(i => (float)i).Reverse()));
            config.Set("Windows", windows);
            config.Set("WindowList", windows.ToList());
            config.Set("Flags", Enumerable.Repeat(true, count).ToArray());
            config.Set("Modes", Enumerable.Repeat(ConfigCollectionMode.Second, count).ToList());
            config.Values = config.Values.Reverse().ToArray();
            var logic = new ConfigCollectionProbeLogic();
            config.Apply(logic);
            CollectionAssert.AreEqual(windows, logic.Windows);
            CollectionAssert.AreEqual(windows, logic.WindowList);
            CollectionAssert.AreEqual(logic.Numbers, logic.Samples);
            Assert.AreEqual(count, logic.Flags.Length);
            Assert.AreEqual(count, logic.Modes.Count);
            Assert.IsTrue(logic.Flags.All(flag => flag));
            Assert.IsTrue(logic.Modes.All(mode => mode == ConfigCollectionMode.Second));
            Assert.IsNull(logic.Optional, "Unconfigured null collections remain uninitialized.");
            config.Set("Windows", windows.Take(1).ToArray());
            config.Set("WindowList", new List<ConfigCollectionWindow>());
            config.Apply(logic);
            Assert.AreEqual(Math.Min(1, count), logic.Windows.Length);
            Assert.IsEmpty(logic.WindowList);
            Assert.IsFalse(config.Values.Any(entry => entry.StartsWith("Windows[1]")));
            var valid = config.Values.ToArray();
            Assert.Throws<ArgumentException>(() => config.Set("Windows", new int[1, 1]));
            Assert.Throws<ArgumentException>(() => config.Set("Windows", new List<int[]> { new int[1] }));
            CollectionAssert.AreEqual(valid, config.Values);
        }

        [TestCase("Windows.$count=-1")]
        [TestCase("Windows.$count=invalid")]
        [TestCase("Windows[5].startSeconds=1")]
        [TestCase("Windows[0].startSeconds=invalid")]
        [TestCase("Windows[0].Missing=1")]
        [TestCase("Speed=invalid")]
        public void InvalidCollectionConfigIsAtomicAndRejectedDuringFreeze(string invalid)
        {
            var logic = new ConfigCollectionProbeLogic();
            var original = logic.Windows;
            var config = new CoCoFieldConfig { Values = new[] { "Speed=2.5", invalid } };
            Assert.Catch(() => config.Apply(logic));
            Assert.AreEqual(6f, logic.Speed);
            Assert.AreSame(original, logic.Windows);
            var provider = CoCoStandardBindingProvider.Build(new[] { typeof(ConfigCollectionProbeLogic).Assembly });
            StandardDescriptors.TryCreate(typeof(ConfigCollectionProbeLogic), "ConfigCollectionProbe", out var id);
            Assert.IsFalse(provider.Catalog.TryFreezeStateConfig(id, config, out _, out var diagnostic));
            Assert.IsFalse(diagnostic.IsNone);
        }

        [Test]
        public void LegacyLeavesDefaultsAndFrozenCollectionCopiesRemainCompatible()
        {
            var config = new CoCoFieldConfig { Values = new[] { "Speed=2.5", "Window.startSeconds=0.25" } };
            var logic = new ConfigCollectionProbeLogic();
            config.Apply(logic);
            Assert.AreEqual(2.5f, logic.Speed);
            Assert.AreEqual(0.25f, logic.Window.startSeconds);
            CollectionAssert.AreEqual(new[] { 7f }, logic.Numbers);
            config.Set("Numbers", new[] { 1f, 2f });
            var provider = CoCoStandardBindingProvider.Build(new[] { typeof(ConfigCollectionProbeLogic).Assembly });
            StandardDescriptors.TryCreate(typeof(ConfigCollectionProbeLogic), "ConfigCollectionProbe", out var id);
            Assert.IsTrue(provider.Catalog.TryFreezeStateConfig(id, config, out var snapshot, out var diagnostic), diagnostic.Message);
            var original = config.Values.ToArray();
            config.Values[0] = "Speed=99";
            Assert.IsTrue(snapshot.TryReadArray(CoCoFieldConfigSchema.Values, out var frozen));
            CollectionAssert.AreEqual(original, frozen.ToArray());
            var restored = frozen.ToArray();
            restored[0] = "Speed=88";
            Assert.AreEqual("Speed=88", restored[0]);
            Assert.AreEqual(original[0], frozen[0]);
        }

        [UnityTest]
        public IEnumerator ConfigCollectionsBindPerNodeAndPerHost()
        {
            var type = typeof(ConfigCollectionProbeLogic);
            var provider = CoCoStandardBindingProvider.Build(new[] { type.Assembly });
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(provider, out var diagnostic), diagnostic.Message);
            StandardDescriptors.TryCreate(type, "ConfigCollectionProbe", out var descriptor);
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            for (ulong i = 0; i < 3; i++)
            {
                var config = new CoCoFieldConfig();
                int count = i == 2 ? 3 : (int)i;
                var windows = Enumerable.Repeat(new ConfigCollectionWindow { startSeconds = i + 0.5f }, count).ToArray();
                config.Set("Numbers", Enumerable.Repeat((float)i, count).ToArray());
                config.Set("Samples", Enumerable.Repeat((float)i, count).ToList());
                config.Set("Windows", windows);
                config.Set("WindowList", windows.ToList());
                var stateId = new CoCoSerializedId128(11 + i, 12);
                var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(21 + i, 22), "Layer " + i);
                layer.InitialStateId = stateId;
                layer.States.Add(new CoCoStateGraphStateRecord(stateId, default, "Probe",
                    new CoCoSerializedId128(descriptor.High, descriptor.Low), config));
                asset.Layers.Add(layer);
            }
            var hosts = new List<CoCoStateGraphHost>();
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Config collections " + i);
                _objects.Add(go);
                var host = go.AddComponent<CoCoStateGraphHost>();
                SetField(host, "stateGraphAsset", asset);
                SetField(host, "autoStart", false);
                SetField(host, "driver", CoCoStateGraphDriver.Manual);
                hosts.Add(host);
            }
            yield return null;
            foreach (var host in hosts)
            {
                Assert.IsTrue(host.TryStart(out diagnostic), diagnostic.Message);
            }
            ((CoCoFieldConfig)asset.Layers[1].States[0].Config).Set("Windows", Array.Empty<ConfigCollectionWindow>());
            foreach (var host in hosts) Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            var seen = ConfigCollectionProbeLogic.Seen;
            Assert.AreEqual(6, seen.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 0, 1, 3 }, seen.Select(logic => logic.Numbers.Length));
            foreach (var logic in seen)
            {
                CollectionAssert.AreEqual(logic.Numbers, logic.Samples);
                CollectionAssert.AreEqual(logic.Windows, logic.WindowList);
            }
            Assert.AreNotSame(seen[1].Numbers, seen[4].Numbers);
            Assert.AreNotSame(seen[1].WindowList, seen[4].WindowList);
            seen[1].Numbers[0] = 99f;
            seen[1].WindowList.Clear();
            Assert.AreEqual(1f, seen[4].Numbers[0]);
            Assert.AreEqual(1.5f, seen[4].WindowList[0].startSeconds);
            Assert.AreEqual(2.5f, seen[2].WindowList[0].startSeconds);
        }
    }
}
