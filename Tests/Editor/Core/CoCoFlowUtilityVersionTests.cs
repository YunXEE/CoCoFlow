using System;
using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace CoCoFlow.Editor.Core.Tests
{
    public sealed class CoCoFlowUtilityVersionTests
    {
        [Test]
        public void PackageAndLocalizationVersionsAreFrozen()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(CoCoFlowUtility).Assembly);
            Assert.IsNotNull(packageInfo);
            Assert.AreEqual("0.4.1", packageInfo.version);

            string manifest = File.ReadAllText(
                Path.Combine(packageInfo.resolvedPath, "package.json"));
            StringAssert.Contains(
                "\"com.unity.localization\": \"1.5.9\"",
                manifest);
        }

        [Test]
        public void AtomicReplacementValidatesAndKeepsAReadableBackup()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CoCoFlow-Atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "manifest.json");
            File.WriteAllText(path, "{\"value\":1}");

            try
            {
                Assert.IsTrue(CoCoAtomicFileTransaction.TryReplaceUtf8(
                    path,
                    "{\"value\":2}",
                    value => value.Contains("\"value\":2"),
                    out string backupPath,
                    out string error),
                    error);
                Assert.AreEqual("{\"value\":2}", File.ReadAllText(path));
                Assert.IsTrue(File.Exists(backupPath));
                Assert.AreEqual("{\"value\":1}", File.ReadAllText(backupPath));

                Assert.IsFalse(CoCoAtomicFileTransaction.TryReplaceUtf8(
                    path,
                    "invalid",
                    value => value.StartsWith("{", StringComparison.Ordinal),
                    out _,
                    out error));
                Assert.IsNotEmpty(error);
                Assert.AreEqual("{\"value\":2}", File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestCase("2.9.0")]
        [TestCase("2.9.1-preview")]
        [TestCase("2.9.1-preview.1")]
        public void AddressablesVersionsBelowMinimumAreRejected(string version)
        {
            Assert.That(
                AddressablesVersionPolicy.Evaluate(version),
                Is.EqualTo(AddressablesVersionCompatibility.BelowMinimum));
        }

        [TestCase("2.9.1")]
        [TestCase("2.99.0")]
        [TestCase("3.0.0-preview")]
        [TestCase("3.0.0-preview.1")]
        [TestCase("2.9.1+manifest.4")]
        public void AddressablesVersionsInsideFrozenRangeAreSupported(string version)
        {
            Assert.That(
                AddressablesVersionPolicy.Evaluate(version),
                Is.EqualTo(AddressablesVersionCompatibility.Supported));
        }

        [TestCase("3.0.0")]
        [TestCase("3.0.1")]
        [TestCase("4.0.0")]
        public void AddressablesVersionsAtOrAboveMaximumAreRejected(string version)
        {
            Assert.That(
                AddressablesVersionPolicy.Evaluate(version),
                Is.EqualTo(AddressablesVersionCompatibility.AtOrAboveMaximum));
        }

        [TestCase("")]
        [TestCase("file:../Addressables")]
        [TestCase("https://example.invalid/addressables.git")]
        [TestCase("2.9")]
        [TestCase("2. 9.1")]
        [TestCase("2.9.01")]
        [TestCase("2.9.1+")]
        [TestCase("2.9.1+meta..x")]
        [TestCase("2.9.1+meta+extra")]
        [TestCase("2.9.1-预览")]
        public void UnverifiableAddressablesVersionsRemainUnknown(string version)
        {
            Assert.That(
                AddressablesVersionPolicy.Evaluate(version),
                Is.EqualTo(AddressablesVersionCompatibility.Unknown));
        }
    }
}
