using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Input
{
    [DisallowMultipleComponent]
    public sealed class InputIntent : MonoBehaviour,
        ICoCoIntentFrameSource<InputIntentData>, ICoCoCursorIntentSource<InputIntentData>
    {
        [SerializeField] private InputReader reader;

        public void SetReader(InputReader value)
        {
            reader = value;
        }

        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out InputIntentData intent) => TrySample(0UL, out intent);

        void ICoCoCursorIntentSource<InputIntentData>.AttachCursor(CoCoIntentSourceCursor cursor) =>
            AttachCursor(cursor);

        bool ICoCoCursorIntentSource<InputIntentData>.TrySample(
            CoCoIntentSourceCursor cursor, out InputIntentData intent)
        {
            AttachCursor(cursor);
            bool sampled = TrySample(cursor.Committed, out intent);
            cursor.Prepared = sampled && !intent.Changes.IsEmpty
                ? intent.Changes.Span[^1].Sequence
                : cursor.Committed;
            return sampled;
        }

        private void AttachCursor(CoCoIntentSourceCursor cursor)
        {
            if (reader != null)
            {
                reader.AttachCursor(cursor);
            }
            else
            {
                cursor.Reader = null;
            }
        }

        private bool TrySample(ulong delivered, out InputIntentData intent)
        {
            intent = default;
            return isActiveAndEnabled &&
                   reader != null &&
                   reader.TrySnapshot(delivered, out intent);
        }
    }
}
