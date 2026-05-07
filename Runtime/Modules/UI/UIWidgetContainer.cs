using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.UI
{
    public enum WidgetContainerLayout { Column, Row, Grid }
    public enum WidgetContainerMode { Static, Dynamic }
    public enum WidgetContainerAnchor
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public class UIWidgetContainer : MonoBehaviour
    {
        [Header("Mode & Rules")]
        [SerializeField] private WidgetContainerMode mode = WidgetContainerMode.Static;

        public WidgetContainerMode Mode => mode;

        [Tooltip("仅在动态模式下生效，预先挖好的坑位数量")]
        [Min(1)] [SerializeField] private int placeholderCount = 5;

        [Tooltip("显式受控的 Widget 列表。容器只会对列表内的元素进行排版。")]
        [SerializeField] private List<RectTransform> managedItems = new List<RectTransform>();

        [Header("Alignment Settings")]
        [SerializeField] private WidgetContainerLayout layoutType = WidgetContainerLayout.Column;
        [SerializeField] private WidgetContainerAnchor anchor = WidgetContainerAnchor.Center;
        [SerializeField] private Vector2 spacing = new Vector2(10, 10);
        [SerializeField] private Vector2 cellSize = new Vector2(100, 50);
        [Min(1)] [SerializeField] private int gridColumns = 3;

        [SerializeField] private bool showPreview = true;

        private RectTransform _rectTransform;
        private RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = GetComponent<RectTransform>());

        #region Public API
        /// <summary>
        /// 应用布局到受控的子物体
        /// </summary>
        public void ApplyLayout()
        {
            if (mode == WidgetContainerMode.Dynamic || managedItems == null) return;

            var rects = CalculateLayoutRects();
            int slotIndex = 0;

            for (int i = 0; i < managedItems.Count; i++)
            {
                RectTransform targetWidget = managedItems[i];
                if (targetWidget == null || !targetWidget.gameObject.activeSelf) continue;

                if (slotIndex >= rects.Count) break;

                Vector2 slotCenter = rects[slotIndex].center;
                targetWidget.localPosition = new Vector3(slotCenter.x, slotCenter.y, targetWidget.localPosition.z);

                slotIndex++;
            }
        }
        #endregion

        #region Internal Logic
        /// <summary>
        /// 获取当前参与排版的实际数量
        /// </summary>
        private int GetLayoutCount()
        {
            if (mode == WidgetContainerMode.Dynamic) return placeholderCount;

            int count = 0;
            if (managedItems != null)
            {
                foreach (var item in managedItems)
                {
                    if (item != null && item.gameObject.activeSelf) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 计算所有坑位的布局矩形
        /// </summary>
        private List<Rect> CalculateLayoutRects()
        {
            List<Rect> rects = new List<Rect>();
            int elementCount = GetLayoutCount();
            if (elementCount <= 0) return rects;

            // 1. 计算总宽高
            float totalWidth = 0f;
            float totalHeight = 0f;

            if (layoutType == WidgetContainerLayout.Column)
            {
                totalWidth = cellSize.x;
                totalHeight = (elementCount * cellSize.y) + ((elementCount - 1) * spacing.y);
            }
            else if (layoutType == WidgetContainerLayout.Row)
            {
                totalWidth = (elementCount * cellSize.x) + ((elementCount - 1) * spacing.x);
                totalHeight = cellSize.y;
            }
            else if (layoutType == WidgetContainerLayout.Grid)
            {
                int rows = Mathf.CeilToInt((float)elementCount / gridColumns);
                int actualCols = Mathf.Min(elementCount, gridColumns);
                totalWidth = (actualCols * cellSize.x) + ((actualCols - 1) * spacing.x);
                totalHeight = (rows * cellSize.y) + ((rows - 1) * spacing.y);
            }

            // 2. 推算起始坐标
            Rect cRect = RectTransform.rect;
            float startX = 0f, startY = 0f;

            switch (anchor)
            {
                case WidgetContainerAnchor.TopLeft:      startX = cRect.xMin; startY = cRect.yMax; break;
                case WidgetContainerAnchor.TopCenter:    startX = cRect.center.x - (totalWidth / 2f); startY = cRect.yMax; break;
                case WidgetContainerAnchor.TopRight:     startX = cRect.xMax - totalWidth; startY = cRect.yMax; break;
                case WidgetContainerAnchor.MiddleLeft:   startX = cRect.xMin; startY = cRect.center.y + (totalHeight / 2f); break;
                case WidgetContainerAnchor.Center:       startX = cRect.center.x - (totalWidth / 2f); startY = cRect.center.y + (totalHeight / 2f); break;
                case WidgetContainerAnchor.MiddleRight:  startX = cRect.xMax - totalWidth; startY = cRect.center.y + (totalHeight / 2f); break;
                case WidgetContainerAnchor.BottomLeft:   startX = cRect.xMin; startY = cRect.yMin + totalHeight; break;
                case WidgetContainerAnchor.BottomCenter: startX = cRect.center.x - (totalWidth / 2f); startY = cRect.yMin + totalHeight; break;
                case WidgetContainerAnchor.BottomRight:  startX = cRect.xMax - totalWidth; startY = cRect.yMin + totalHeight; break;
            }

            // 3. 生成每个坑位
            float currentX = startX;
            float currentY = startY;

            for (int i = 0; i < elementCount; i++)
            {
                rects.Add(new Rect(currentX, currentY - cellSize.y, cellSize.x, cellSize.y));

                if (layoutType == WidgetContainerLayout.Column)
                {
                    currentY -= (cellSize.y + spacing.y);
                }
                else if (layoutType == WidgetContainerLayout.Row)
                {
                    currentX += (cellSize.x + spacing.x);
                }
                else if (layoutType == WidgetContainerLayout.Grid)
                {
                    currentX += (cellSize.x + spacing.x);
                    if ((i + 1) % gridColumns == 0) // 换行
                    {
                        currentX = startX;
                        currentY -= (cellSize.y + spacing.y);
                    }
                }
            }
            return rects;
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!Application.isPlaying && mode == WidgetContainerMode.Static)
            {
                ApplyLayout();
            }
        }

        private void OnDrawGizmos()
        {
            if (!showPreview) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.4f, 0.7f, 0.5f);
            Color wireColor = new Color(1f, 0.2f, 0.6f, 0.8f);

            var rects = CalculateLayoutRects();
            foreach (var rect in rects)
            {
                Vector3 center = new Vector3(rect.center.x, rect.center.y, 0);
                Vector3 size = new Vector3(rect.width, rect.height, 0);

                Gizmos.DrawCube(center, size);
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(center, size);
                Gizmos.color = new Color(1f, 0.4f, 0.7f, 0.5f);
            }
        }
#endif
        #endregion
    }
}


