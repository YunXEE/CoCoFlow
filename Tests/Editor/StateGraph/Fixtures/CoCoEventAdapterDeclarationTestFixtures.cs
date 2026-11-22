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

    public readonly struct AlternateTestGraphEvent { }

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

}
