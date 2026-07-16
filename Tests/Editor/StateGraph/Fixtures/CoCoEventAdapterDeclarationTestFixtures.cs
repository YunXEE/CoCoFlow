namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public readonly struct TestGraphEvent
    {
        public TestGraphEvent(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public readonly struct AlternateTestGraphEvent
    {
        public AlternateTestGraphEvent(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public readonly struct AlternateTestIntent
    {
        public AlternateTestIntent(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public readonly struct AlternateTestIntentReducer : ICoCoIntentReducer<AlternateTestIntent>
    {
        public AlternateTestIntent Reduce(
            in AlternateTestIntent current,
            in AlternateTestIntent candidate) =>
            new AlternateTestIntent(current.Value + candidate.Value);
    }

    public sealed class AlternateTestIntentReducerFactory :
        ICoCoIntentReducerFactory<AlternateTestIntent, AlternateTestIntentReducer>
    {
        public AlternateTestIntentReducer Create(CoCoGraphInstanceId graphInstanceId) =>
            new AlternateTestIntentReducer();
    }

    public sealed class CountingTestEventToIntentAdapter :
        ICoCoEventToIntentAdapter<TestGraphEvent, TestIntent>
    {
        public CountingTestEventToIntentAdapter()
        {
            Constructed++;
        }

        public static int Constructed { get; private set; }
        public static int Projected { get; private set; }

        public static void Reset()
        {
            Constructed = 0;
            Projected = 0;
        }

        public bool TryProject(
            in CoCoEventPacket<TestGraphEvent> packet,
            out TestIntent intent)
        {
            Projected++;
            intent = new TestIntent(packet.Payload.Value);
            return true;
        }
    }
}
