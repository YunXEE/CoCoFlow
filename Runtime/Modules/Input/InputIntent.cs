using CoCoFlow.Runtime.Input.Contracts;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

// Unity Host 在步进前通过此来源读取 InputReader 的快照，成功提交后才确认采样游标。
namespace CoCoFlow.Runtime.Modules.Input
{
    /// <summary>将绑定的 InputReader 接入 Host 的采样与提交游标。</summary>
    [DisallowMultipleComponent]
    public sealed class InputIntent : MonoBehaviour,
        IIntentFrameSource<InputIntentContract>, ICursorIntentSource<InputIntentContract>
    {

        #region Public API

        /// <summary>选择提供动作快照的 InputReader，下次采样从所选读取器取得输入。</summary>
        public void SetReader(InputReader value)
        {
            reader = value;
        }

        /// <summary>从已绑定并启用的读取器取得动作快照，无法采样时返回 false。</summary>
        public bool TrySample(
            in TickFrame tickFrame,
            out InputIntentContract intent) => TrySample(0UL, out intent);

        void ICursorIntentSource<InputIntentContract>.AttachCursor(IntentSourceCursor cursor) =>
            AttachCursor(cursor);

        bool ICursorIntentSource<InputIntentContract>.TrySample(
            IntentSourceCursor cursor, out InputIntentContract intent)
        {
            AttachCursor(cursor);
            bool sampled = TrySample(cursor.Committed, out intent);
            cursor.Prepared = sampled && !intent.Changes.IsEmpty
                ? intent.Changes.Span[^1].Sequence
                : cursor.Committed;
            return sampled;
        }

        #endregion

        #region Internal Logic

        [SerializeField] private InputReader reader;

        private void AttachCursor(IntentSourceCursor cursor)
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

        private bool TrySample(ulong delivered, out InputIntentContract intent)
        {
            intent = default;
            return isActiveAndEnabled &&
                   reader != null &&
                   reader.TrySnapshot(delivered, out intent);
        }

        #endregion
    }
}
