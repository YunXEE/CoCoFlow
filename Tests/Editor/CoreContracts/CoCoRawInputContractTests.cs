using CoCoFlow.Runtime.Core;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.CoreContracts
{
    public sealed class CoCoRawInputContractTests
    {
        [Test]
        public void FixedString_RoundTripsAscii()
        {
            var value = CoCoFixedString64.FromString("Jump");
            Assert.AreEqual(4, value.Length);
            Assert.AreEqual("Jump", value.ToString());
            Assert.AreEqual(value, CoCoFixedString64.FromString("Jump"));
            Assert.AreNotEqual(
                value,
                CoCoFixedString64.FromString("Attack"));
        }

        [Test]
        public void FixedString_EmptyAndTruncation()
        {
            Assert.AreEqual(0, CoCoFixedString64.FromString("").Length);
            Assert.AreEqual(
                string.Empty,
                CoCoFixedString64.FromString(null).ToString());

            var truncated = CoCoFixedString64.FromString(
                new string('a', CoCoFixedString64.Capacity + 10));
            Assert.AreEqual(CoCoFixedString64.Capacity, truncated.Length);
            Assert.IsFalse(truncated.TryGetByte(CoCoFixedString64.Capacity, out _));
            Assert.IsTrue(truncated.TryGetByte(0, out byte first));
            Assert.AreEqual((byte)'a', first);
        }

        [Test]
        public void FixedString_EqualsUsesBytesNotObjectIdentity()
        {
            Assert.AreEqual(
                CoCoFixedString64.FromString("Move"),
                CoCoFixedString64.FromString("Move"));
            Assert.AreNotEqual(
                CoCoFixedString64.FromString("Move"),
                CoCoFixedString64.FromString("Mov"));
            Assert.AreNotEqual(
                CoCoFixedString64.FromString(""),
                CoCoFixedString64.FromString("M"));
        }

        [Test]
        public void Intent_TryGetAndCapacityBoundary()
        {
            var intent = new RawInputIntent();
            Assert.IsFalse(intent.TryGet(0, out _));
            Assert.AreEqual(0, intent.Count);

            for (int index = 0; index < RawInputIntent.RecordCapacity; index++)
            {
                intent.Set(index, new RawInputRecord
                {
                    Action = CoCoFixedString64.FromString("A" + index),
                    Sequence = (ulong)(index + 1)
                });
            }

            intent.Count = RawInputIntent.RecordCapacity;
            Assert.IsTrue(intent.TryGet(7, out RawInputRecord last));
            Assert.AreEqual("A7", last.Action.ToString());
            Assert.IsFalse(intent.TryGet(8, out _));
        }

        [Test]
        public void Intent_TryFindMatchesNameAndPhaseInArrivalOrder()
        {
            var intent = new RawInputIntent();
            intent.Set(intent.Count++, new RawInputRecord
            {
                Action = CoCoFixedString64.FromString("Move"),
                Phase = RawInputPhase.Held,
                Sequence = 1
            });
            intent.Set(intent.Count++, new RawInputRecord
            {
                Action = CoCoFixedString64.FromString("Jump"),
                Phase = RawInputPhase.Performed,
                Sequence = 2
            });
            intent.Set(intent.Count++, new RawInputRecord
            {
                Action = CoCoFixedString64.FromString("Attack"),
                Phase = RawInputPhase.Performed,
                Sequence = 3
            });

            Assert.IsTrue(
                intent.TryFind("Jump", RawInputPhase.Performed, out RawInputRecord jump));
            Assert.AreEqual(2UL, jump.Sequence);

            // Same name, wrong phase -> not found.
            Assert.IsFalse(intent.TryFind("Jump", RawInputPhase.Canceled, out _));
            // Held semantics: released actions are simply absent.
            Assert.IsFalse(intent.TryFind("Move", RawInputPhase.Performed, out _));
        }

        [Test]
        public void Intent_TryFindReturnsFirstDuplicateInArrivalOrder()
        {
            var intent = new RawInputIntent();
            intent.Set(intent.Count++, new RawInputRecord
            {
                Action = CoCoFixedString64.FromString("Jump"),
                Phase = RawInputPhase.Started,
                Sequence = 1
            });
            intent.Set(intent.Count++, new RawInputRecord
            {
                Action = CoCoFixedString64.FromString("Jump"),
                Phase = RawInputPhase.Performed,
                Sequence = 2
            });

            Assert.IsTrue(
                intent.TryFind("Jump", RawInputPhase.Performed, out RawInputRecord record));
            Assert.AreEqual(2UL, record.Sequence);
        }
    }
}
