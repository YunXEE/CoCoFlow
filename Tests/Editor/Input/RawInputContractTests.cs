using CoCoFlow.Runtime.Input.Contracts;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.Input
{
    public sealed class RawInputContractTests
    {
        [Test]
        public void FixedString_RoundTripsAscii()
        {
            var value = FixedString64.FromString("Jump");
            Assert.AreEqual(4, value.Length);
            Assert.AreEqual("Jump", value.ToString());
            Assert.AreEqual(value, FixedString64.FromString("Jump"));
            Assert.AreNotEqual(
                value,
                FixedString64.FromString("Attack"));
        }

        [Test]
        public void FixedString_EmptyAndTruncation()
        {
            Assert.AreEqual(0, FixedString64.FromString("").Length);
            Assert.AreEqual(
                string.Empty,
                FixedString64.FromString(null).ToString());

            var truncated = FixedString64.FromString(
                new string('a', FixedString64.Capacity + 10));
            Assert.AreEqual(FixedString64.Capacity, truncated.Length);
            Assert.IsFalse(truncated.TryGetByte(FixedString64.Capacity, out _));
            Assert.IsTrue(truncated.TryGetByte(0, out byte first));
            Assert.AreEqual((byte)'a', first);
        }

        [Test]
        public void FixedString_EqualsUsesBytesNotObjectIdentity()
        {
            Assert.AreEqual(
                FixedString64.FromString("Move"),
                FixedString64.FromString("Move"));
            Assert.AreNotEqual(
                FixedString64.FromString("Move"),
                FixedString64.FromString("Mov"));
            Assert.AreNotEqual(
                FixedString64.FromString(""),
                FixedString64.FromString("M"));
        }

        [Test]
        public void Intent_TryGetReadsChangesThenCurrentBeyondEightRecords()
        {
            Assert.IsFalse(default(InputIntentContract).TryGet(0, out _));
            var changes = new RawInputRecord[10];
            for (int index = 0; index < changes.Length; index++)
            {
                changes[index] = Record("Jump", RawInputPhase.Performed, (ulong)(index + 1));
            }

            var intent = new InputIntentContract(default, changes, new[] { Record("Move", RawInputPhase.Held) });
            Assert.AreEqual(11, intent.Count);
            Assert.IsTrue(intent.TryGet(9, out RawInputRecord lastChange));
            Assert.AreEqual(10UL, lastChange.Sequence);
            Assert.IsTrue(intent.TryGet(10, out RawInputRecord current));
            Assert.AreEqual("Move", current.Action.ToString());
            Assert.IsFalse(intent.TryGet(-1, out _));
            Assert.IsFalse(intent.TryGet(11, out _));
        }

        [Test]
        public void Intent_TryFindMatchesPhaseAndReturnsFirstDuplicate()
        {
            var intent = new InputIntentContract(default, new[]
            {
                Record("Jump", RawInputPhase.Started, 1),
                Record("Jump", RawInputPhase.Performed, 2),
                Record("Jump", RawInputPhase.Performed, 3)
            }, new[] { Record("Move", RawInputPhase.Held) });

            Assert.IsTrue(intent.TryFind("Jump", RawInputPhase.Performed, out RawInputRecord record));
            Assert.AreEqual(2UL, record.Sequence);
            Assert.IsFalse(intent.TryFind("Jump", RawInputPhase.Canceled, out _));
            Assert.IsTrue(intent.TryFind("Move", RawInputPhase.Held, out _));
        }

        private static RawInputRecord Record(string action, RawInputPhase phase, ulong sequence = 0) =>
            new RawInputRecord
            {
                Action = FixedString64.FromString(action),
                Phase = phase,
                Sequence = sequence
            };
    }
}
