using NUnit.Framework;

namespace CoCoFlow.Editor.Core.Tests
{
    public sealed class CoCoFlowSetupAssistantVersionTests
    {
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
