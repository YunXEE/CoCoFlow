using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// StateGraph 自研画布（P03 重做，D2：不迁 GraphView）。
    /// 指针语义与既有交互测试逐条锚定：中键平移、滚轮缩放(0.25–2)、
    /// 卡片拖动（错误指针忽略/取消还原/捕获丢失还原/单次提交）、
    /// 过渡拖拽（捕获/取消/恰完成一次）、右键上下文菜单。
    /// D8：边呈现照搬 Animator——中心锚定、双向平行偏移、方向三角、
    /// 自环回环、点击线选中 Transition、双击 Composite 下钻。
    /// </summary>
    internal sealed class CoCoStateGraphEditorCanvas : VisualElement, IDisposable
    {
        private const float CanvasSize = 4096f;
        private const float CardWidth = 188f;
        private const float CardHeight = 96f;
        private const float ParallelSpacing = 16f;
        private const float TriangleLength = 9f;
        private const float TriangleWidth = 7f;
        private const float LoopHeight = 44f;
        private const float LoopRadius = 30f;
        private const float GridTileSize = 32f;

        private static Color EdgeColor => new Color(0.44f, 0.68f, 0.86f, 0.9f);
        private static Color SelectedEdgeColor => new Color(1f, 0.72f, 0.2f, 1f);

        private static Texture2D dotGridTexture;

        /// <summary>点阵网格纹理（维护者反馈 3：格子纸点点背景；平铺在 content 上随平移缩放）。</summary>
        private static Texture2D DotGridTexture
        {
            get
            {
                if (dotGridTexture != null)
                {
                    return dotGridTexture;
                }

                const int size = (int)GridTileSize;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point
                };
                var pixels = new Color32[size * size];
                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                }

                // 瓦片中心一个 2×2 淡点。
                byte dot = 46;
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        pixels[(size / 2 - 1 + dy) * size + (size / 2 - 1 + dx)] =
                            new Color32(dot, dot, dot, 255);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                dotGridTexture = texture;
                return dotGridTexture;
            }
        }

        private readonly CoCoStateGraphEditorController controller;
        private readonly VisualElement content;
        private readonly VisualElement edgeLayer;
        private readonly Dictionary<CoCoSerializedId128, Rect> stateRects =
            new Dictionary<CoCoSerializedId128, Rect>();
        private readonly Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord> visibleStates =
            new Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord>();
        private readonly List<EdgeHit> edgeHits = new List<EdgeHit>();

        private bool panning;
        private int panPointerId;
        private Vector2 panPointerStart;
        private CoCoStateGraphCanvasView panStartView;
        private bool transitionDragging;
        private int transitionPointerId;
        private CoCoSerializedId128 transitionSourceStateId;
        private Vector2 transitionPointerPosition;
        private Vector2 transitionDragStartPanel;

        internal CoCoStateGraphEditorCanvas(CoCoStateGraphEditorController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            name = "state-graph-canvas";
            focusable = true;
            style.flexGrow = 1f;
            style.position = Position.Relative;
            style.overflow = Overflow.Hidden;

            content = new VisualElement { name = "state-graph-canvas-content" };
            content.style.position = Position.Absolute;
            content.style.left = 0f;
            content.style.top = 0f;
            content.style.width = CanvasSize;
            content.style.height = CanvasSize;
            content.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);
            Add(content);

            edgeLayer = new VisualElement { name = "state-graph-edges", pickingMode = PickingMode.Ignore };
            edgeLayer.style.position = Position.Absolute;
            edgeLayer.style.left = 0f;
            edgeLayer.style.top = 0f;
            edgeLayer.style.width = CanvasSize;
            edgeLayer.style.height = CanvasSize;
            edgeLayer.generateVisualContent += DrawEdges;
            content.Add(edgeLayer);

            // 点阵背景平铺在 content 上：随平移缩放同步运动（Animator 网格观感）。
            content.style.backgroundImage = new StyleBackground(DotGridTexture);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<WheelEvent>(OnWheel);
            controller.Changed += OnControllerChanged;
            Refresh();
        }

        internal event Action<Vector2> ContextRequested;

        /// <summary>卡片右键（无拖拽位移）：请求该 State 的上下文菜单（建子级/下钻入口）。</summary>
        internal event Action<CoCoSerializedId128> CardContextRequested;

        /// <summary>当前边命中几何数（重绘后更新；测试/诊断用）。</summary>
        internal int EdgeHitCount => edgeHits.Count;

        public void Dispose()
        {
            controller.Changed -= OnControllerChanged;
            CancelTransitionDrag(releasePointer: true);
            CancelPan(releasePointer: true);
            edgeLayer.generateVisualContent -= DrawEdges;
        }

        private void OnControllerChanged(CoCoStateGraphEditorInvalidation invalidation)
        {
            if ((invalidation & CoCoStateGraphEditorInvalidation.Canvas) != 0)
            {
                Refresh();
            }
        }

        internal void Refresh()
        {
            CancelTransitionDrag(releasePointer: true);
            CancelPan(releasePointer: true);
            content.Clear();
            content.Add(edgeLayer);
            stateRects.Clear();
            visibleStates.Clear();
            edgeHits.Clear();

            IReadOnlyList<CoCoStateGraphStateRecord> states = controller.VisibleStates;
            for (int index = 0; index < states.Count; index++)
            {
                CoCoStateGraphStateRecord state = states[index];
                Vector2 position = controller.GetPosition(state, index);
                var card = new CoCoStateGraphStateCard(
                    controller,
                    state,
                    position,
                    OnCardMoved,
                    BeginTransitionDrag,
                    stateId => CardContextRequested?.Invoke(stateId));
                content.Add(card);
                visibleStates[state.StateId] = state;
                stateRects[state.StateId] = new Rect(position, new Vector2(CardWidth, CardHeight));
            }

            if (states.Count == 0)
            {
                var hint = new Label(CoCoEditorLocalization.Text(
                    "Right-click empty canvas to add a State; right-drag from a State to another leaf to add a Transition.",
                    "右键画布空白处添加 State；右键拖拽 State 卡拉出 Transition。"))
                {
                    name = "state-graph-canvas-hint"
                };
                hint.AddToClassList("sg-muted");
                content.Add(hint);
            }

            ApplyView();
            edgeLayer.MarkDirtyRepaint();
        }

        private void OnCardMoved(CoCoSerializedId128 serializedId, Vector2 position, bool commit)
        {
            stateRects[serializedId] = new Rect(position, new Vector2(CardWidth, CardHeight));
            edgeLayer.MarkDirtyRepaint();
            if (!commit ||
                !CoCoStateId.TryCreate(serializedId.High, serializedId.Low, out CoCoStateId stateId))
            {
                return;
            }

            controller.SetPosition(stateId, position);
        }

        // ── D8：边几何（Animator 对齐） ────────────────────

        /// <summary>一次点击命中的边几何缓存（图空间）。</summary>
        private struct EdgeHit
        {
            internal CoCoSerializedId128 TransitionId;
            internal Vector2 Start;
            internal Vector2 End;
            internal bool IsLoop;
            internal Vector2 LoopCenter;
            internal float LoopRadius;
        }

        private void DrawEdges(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            edgeHits.Clear();

            // 按无序端点对分组：同对多条 Transition（含双向）渲染为平行线。
            var groups = new Dictionary<(ulong, ulong, ulong, ulong), List<CoCoStateGraphTransitionRecord>>();
            foreach (CoCoStateGraphTransitionRecord transition in controller.VisibleTransitions)
            {
                if (transition == null ||
                    !stateRects.TryGetValue(transition.SourceStateId, out Rect source) ||
                    !stateRects.TryGetValue(transition.TargetStateId, out Rect target))
                {
                    continue;
                }

                (ulong ah, ulong al, ulong bh, ulong bl) key = transition.SourceStateId.High < transition.TargetStateId.High ||
                    (transition.SourceStateId.High == transition.TargetStateId.High &&
                     transition.SourceStateId.Low < transition.TargetStateId.Low)
                    ? (transition.SourceStateId.High, transition.SourceStateId.Low,
                       transition.TargetStateId.High, transition.TargetStateId.Low)
                    : (transition.TargetStateId.High, transition.TargetStateId.Low,
                       transition.SourceStateId.High, transition.SourceStateId.Low);
                if (!groups.TryGetValue(key, out List<CoCoStateGraphTransitionRecord> list))
                {
                    list = new List<CoCoStateGraphTransitionRecord>();
                    groups[key] = list;
                }

                list.Add(transition);
            }

            foreach (List<CoCoStateGraphTransitionRecord> group in groups.Values)
            {
                // 稳定排序：组内按端点+优先级，保证偏移分布确定性。
                CoCoSerializedId128 orientationSource = group[0].SourceStateId;
                group.Sort((left, right) =>
                {
                    bool leftForward = left.SourceStateId == orientationSource;
                    bool rightForward = right.SourceStateId == orientationSource;
                    if (leftForward != rightForward)
                    {
                        return leftForward ? -1 : 1;
                    }

                    return left.Priority.CompareTo(right.Priority);
                });

                // 组共享法线：以组内首条边方向为基准；反向边若用自身方向算法线，
                // 偏移会在世界空间翻转导致两线重叠（Animator 双向平行线要求同侧基准）。
                if (!stateRects.TryGetValue(group[0].SourceStateId, out Rect canonicalSource) ||
                    !stateRects.TryGetValue(group[0].TargetStateId, out Rect canonicalTarget))
                {
                    continue;
                }

                Vector2 canonicalDirection =
                    (canonicalTarget.center - canonicalSource.center).normalized;
                Vector2 sharedNormal = new Vector2(-canonicalDirection.y, canonicalDirection.x);

                int count = group.Count;
                for (int index = 0; index < count; index++)
                {
                    CoCoStateGraphTransitionRecord transition = group[index];
                    float offset = (index - (count - 1) * 0.5f) * ParallelSpacing;
                    DrawEdge(painter, transition, offset, sharedNormal);
                }
            }

            if (transitionDragging && stateRects.TryGetValue(transitionSourceStateId, out Rect sourceRect))
            {
                // 维护者反馈：预览线从源卡中心出（与已提交边的中心锚定一致）。
                Vector2 previewStart = sourceRect.center;
                painter.strokeColor = SelectedEdgeColor;
                painter.lineWidth = 2f;
                painter.fillColor = SelectedEdgeColor;
                painter.BeginPath();
                painter.MoveTo(previewStart);
                painter.LineTo(transitionPointerPosition);
                painter.Stroke();
                DrawDirectionTriangle(
                    painter,
                    Vector2.Lerp(previewStart, transitionPointerPosition, 0.5f),
                    (transitionPointerPosition - previewStart).normalized);
            }
        }

        private void DrawEdge(
            Painter2D painter,
            CoCoStateGraphTransitionRecord transition,
            float offset,
            Vector2 sharedNormal)
        {
            if (!stateRects.TryGetValue(transition.SourceStateId, out Rect source) ||
                !stateRects.TryGetValue(transition.TargetStateId, out Rect target))
            {
                return;
            }

            bool selected = Matches(transition.TransitionId, controller.Session.SelectedTransitionId);
            painter.strokeColor = selected ? SelectedEdgeColor : EdgeColor;
            painter.fillColor = selected ? SelectedEdgeColor : EdgeColor;
            painter.lineWidth = selected ? 3f : 2f;

            if (transition.SourceStateId == transition.TargetStateId)
            {
                DrawSelfLoop(painter, transition, source);
                return;
            }

            // 中心锚定：两端取卡中心，整体沿组共享法线平移（双向=平行线，Animator 同款）。
            Vector2 a = source.center + sharedNormal * offset;
            Vector2 b = target.center + sharedNormal * offset;

            // 视觉段在卡片边界出/入（线不穿卡身）。
            Vector2 start = a;
            Vector2 end = b;
            if (ClipSegmentToRectExit(a, b, source, out float tExit) &&
                ClipSegmentToRectEnter(a, b, target, out float tEnter) &&
                tEnter > tExit)
            {
                start = Vector2.Lerp(a, b, tExit);
                end = Vector2.Lerp(a, b, tEnter);
            }
            else
            {
                Vector2 fallbackDirection = (target.center - source.center).normalized;
                start = source.center + fallbackDirection * (source.width * 0.5f);
                end = target.center - fallbackDirection * (target.width * 0.5f);
            }

            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
            DrawDirectionTriangle(painter, Vector2.Lerp(start, end, 0.5f), (end - start).normalized);

            edgeHits.Add(new EdgeHit
            {
                TransitionId = transition.TransitionId,
                Start = start,
                End = end
            });
        }

        private void DrawSelfLoop(Painter2D painter, CoCoStateGraphTransitionRecord transition, Rect rect)
        {
            Vector2 topRight = new Vector2(rect.center.x + rect.width * 0.22f, rect.yMin);
            Vector2 topLeft = new Vector2(rect.center.x - rect.width * 0.22f, rect.yMin);
            Vector2 controlRight = topRight + new Vector2(20f, -LoopHeight);
            Vector2 controlLeft = topLeft + new Vector2(-20f, -LoopHeight);

            painter.BeginPath();
            painter.MoveTo(topRight);
            painter.BezierCurveTo(controlRight, controlLeft, topLeft);
            painter.Stroke();

            // 自环三角：贝塞尔中点，方向取中点切线。
            Vector2 loopMid = CubicBezierPoint(0.5f, topRight, controlRight, controlLeft, topLeft);
            Vector2 loopTangent = CubicBezierTangent(0.5f, topRight, controlRight, controlLeft, topLeft);
            DrawDirectionTriangle(painter, loopMid, loopTangent.normalized);

            edgeHits.Add(new EdgeHit
            {
                TransitionId = transition.TransitionId,
                IsLoop = true,
                LoopCenter = new Vector2(rect.center.x, rect.yMin - LoopHeight * 0.62f),
                LoopRadius = LoopRadius,
                Start = topRight,
                End = topLeft
            });
        }

        /// <summary>维护者反馈 1：实心小三角，画在连线中间，指向行进方向。</summary>
        private static void DrawDirectionTriangle(Painter2D painter, Vector2 center, Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            direction.Normalize();
            Vector2 normal = new Vector2(-direction.y, direction.x);
            painter.BeginPath();
            painter.MoveTo(center + direction * (TriangleLength * 0.5f));
            painter.LineTo(center - direction * (TriangleLength * 0.5f) + normal * (TriangleWidth * 0.5f));
            painter.LineTo(center - direction * (TriangleLength * 0.5f) - normal * (TriangleWidth * 0.5f));
            painter.ClosePath();
            painter.Fill();
        }

        private static Vector2 CubicBezierPoint(
            float t,
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3) =>
            (1f - t) * (1f - t) * (1f - t) * p0 +
            3f * (1f - t) * (1f - t) * t * p1 +
            3f * (1f - t) * t * t * p2 +
            t * t * t * p3;

        private static Vector2 CubicBezierTangent(
            float t,
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3) =>
            3f * (1f - t) * (1f - t) * (p1 - p0) +
            6f * (1f - t) * t * (p2 - p1) +
            3f * t * t * (p3 - p2);

        /// <summary>线段 a→b 从内部离开矩形 clip 的参数 t（a 在矩形内）。</summary>
        private static bool ClipSegmentToRectExit(Vector2 a, Vector2 b, Rect clip, out float tExit)
        {
            if (!ClipSegmentToRect(a, b, clip, out float tEnter, out float tLeave))
            {
                tExit = 0f;
                return false;
            }

            tExit = tLeave;
            return tLeave > 0f && tLeave <= 1f;
        }

        /// <summary>线段 a→b 进入矩形 clip 的参数 t（b 在矩形内）。</summary>
        private static bool ClipSegmentToRectEnter(Vector2 a, Vector2 b, Rect clip, out float tEnter)
        {
            if (!ClipSegmentToRect(a, b, clip, out float enter, out float leave))
            {
                tEnter = 1f;
                return false;
            }

            tEnter = enter;
            return enter >= 0f && enter < 1f;
        }

        /// <summary>Liang-Barsky：线段与 AABB 的相交参数区间。</summary>
        private static bool ClipSegmentToRect(
            Vector2 a,
            Vector2 b,
            Rect rect,
            out float tEnter,
            out float tLeave)
        {
            tEnter = 0f;
            tLeave = 1f;
            Vector2 delta = b - a;
            float[] p = { -delta.x, delta.x, -delta.y, delta.y };
            float[] q =
            {
                a.x - rect.xMin,
                rect.xMax - a.x,
                a.y - rect.yMin,
                rect.yMax - a.y
            };

            for (int index = 0; index < 4; index++)
            {
                if (Mathf.Approximately(p[index], 0f))
                {
                    if (q[index] < 0f)
                    {
                        return false;
                    }

                    continue;
                }

                float t = q[index] / p[index];
                if (p[index] < 0f)
                {
                    if (t > tLeave)
                    {
                        return false;
                    }

                    if (t > tEnter)
                    {
                        tEnter = t;
                    }
                }
                else
                {
                    if (t < tEnter)
                    {
                        return false;
                    }

                    if (t < tLeave)
                    {
                        tLeave = t;
                    }
                }
            }

            return true;
        }

        /// <summary>D8：点击线=选中该 Transition（距离命中，阈值随缩放换算）。</summary>
        internal bool TryHitEdge(Vector2 graphPosition, float threshold, out CoCoSerializedId128 transitionId)
        {
            transitionId = default;
            float best = threshold;
            bool found = false;
            foreach (EdgeHit hit in edgeHits)
            {
                float distance;
                if (hit.IsLoop)
                {
                    distance = Mathf.Abs(Vector2.Distance(graphPosition, hit.LoopCenter) - hit.LoopRadius);
                    // 回环中心区域也算命中（环内部点击）。
                    if (Vector2.Distance(graphPosition, hit.LoopCenter) <= hit.LoopRadius)
                    {
                        distance = 0f;
                    }
                }
                else
                {
                    distance = DistancePointToSegment(graphPosition, hit.Start, hit.End);
                }

                if (distance <= best)
                {
                    best = distance;
                    transitionId = hit.TransitionId;
                    found = true;
                }
            }

            return found;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0f)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        // ── 指针交互（语义与既有测试逐条锚定） ────────────

        private void BeginTransitionDrag(
            CoCoSerializedId128 sourceStateId,
            int pointerId,
            Vector2 panelPosition)
        {
            if (!CoCoStateGraphAuthoringOperations.CanEdit(out _))
            {
                return;
            }

            if (transitionDragging || panning)
            {
                return;
            }

            transitionDragging = true;
            transitionPointerId = pointerId;
            transitionSourceStateId = sourceStateId;
            transitionPointerPosition = ToGraphPosition(PanelToLocal(panelPosition));
            transitionDragStartPanel = panelPosition;
            this.CapturePointer(pointerId);
            edgeLayer.MarkDirtyRepaint();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target != this && evt.target != content && evt.target != edgeLayer)
            {
                return;
            }

            if (evt.button == 0)
            {
                // D8：左键点击边=选中 Transition；未命中维持现状（不启动任何手势）。
                if (TryHitEdge(
                        ToGraphPosition(evt.localPosition),
                        6f / Mathf.Max(CurrentView.Zoom, 0.01f),
                        out CoCoSerializedId128 hitId) &&
                    CoCoTransitionId.TryCreate(hitId.High, hitId.Low, out CoCoTransitionId hitTransitionId))
                {
                    controller.SelectTransition(hitTransitionId);
                    evt.StopPropagation();
                }

                return;
            }

            if (evt.button == 2)
            {
                if (panning || transitionDragging)
                {
                    return;
                }

                panning = true;
                panPointerId = evt.pointerId;
                panPointerStart = evt.position;
                panStartView = CurrentView;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (evt.button == 1)
            {
                TryRequestContext(ToGraphPosition(evt.localPosition));
                evt.StopPropagation();
            }
        }

        internal bool TryRequestContext(Vector2 graphPosition)
        {
            if (!CoCoStateGraphAuthoringOperations.CanEdit(out _))
            {
                return false;
            }

            ContextRequested?.Invoke(graphPosition);
            return true;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (transitionDragging && evt.pointerId == transitionPointerId)
            {
                transitionPointerPosition = ToGraphPosition(PanelToLocal(evt.position));
                edgeLayer.MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            if (!panning || evt.pointerId != panPointerId)
            {
                return;
            }

            Vector2 pan = panStartView.Pan + (Vector2)evt.position - panPointerStart;
            SetView(new CoCoStateGraphCanvasView(pan, CurrentView.Zoom), save: false);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (transitionDragging && evt.pointerId == transitionPointerId)
            {
                transitionPointerPosition = ToGraphPosition(PanelToLocal(evt.position));
                transitionDragging = false;
                ReleaseCapturedPointer(transitionPointerId);
                TryCompleteTransitionDrag(evt.position);
                ResetTransitionDrag();
                edgeLayer.MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            if (!panning || evt.pointerId != panPointerId)
            {
                return;
            }

            panning = false;
            ReleaseCapturedPointer(panPointerId);
            controller.Session.SetCanvasView(
                controller.Session.SelectedLayerId,
                controller.Session.DrillRootStateId,
                CurrentView);
            controller.Session.Save();
            panPointerId = 0;
            panPointerStart = default;
            panStartView = default;
            evt.StopPropagation();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (transitionDragging && evt.pointerId == transitionPointerId)
            {
                CancelTransitionDrag(releasePointer: true);
                evt.StopPropagation();
                return;
            }

            if (panning && evt.pointerId == panPointerId)
            {
                CancelPan(releasePointer: true);
                evt.StopPropagation();
            }
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (transitionDragging && evt.pointerId == transitionPointerId)
            {
                CancelTransitionDrag(releasePointer: false);
                evt.StopPropagation();
                return;
            }

            if (panning && evt.pointerId == panPointerId)
            {
                CancelPan(releasePointer: false);
                evt.StopPropagation();
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            float zoom = Mathf.Clamp(CurrentView.Zoom * (evt.delta.y > 0f ? 0.9f : 1.1f), 0.25f, 2f);
            SetView(new CoCoStateGraphCanvasView(CurrentView.Pan, zoom), save: true);
            evt.StopPropagation();
        }

        private CoCoStateGraphCanvasView CurrentView =>
            controller.Session.GetCanvasView(
                controller.Session.SelectedLayerId,
                controller.Session.DrillRootStateId);

        private void SetView(CoCoStateGraphCanvasView view, bool save)
        {
            view = view.Clamp();
            controller.Session.SetCanvasView(
                controller.Session.SelectedLayerId,
                controller.Session.DrillRootStateId,
                view);
            ApplyView();
            if (save)
            {
                controller.Session.Save();
            }
        }

        private void ApplyView()
        {
            CoCoStateGraphCanvasView view = CurrentView;
            content.transform.position = new Vector3(view.Pan.x, view.Pan.y, 0f);
            content.transform.scale = new Vector3(view.Zoom, view.Zoom, 1f);
        }

        private Vector2 ToGraphPosition(Vector2 localPosition)
        {
            CoCoStateGraphCanvasView view = CurrentView;
            return (localPosition - view.Pan) / view.Zoom;
        }

        private Vector2 PanelToLocal(Vector2 panelPosition) =>
            panelPosition - worldBound.position;

        private void TryCompleteTransitionDrag(Vector2 endPanelPosition)
        {
            CoCoSerializedId128 targetStateId = default;
            foreach (KeyValuePair<CoCoSerializedId128, Rect> entry in stateRects)
            {
                if (entry.Value.Contains(transitionPointerPosition))
                {
                    targetStateId = entry.Key;
                    break;
                }
            }

            if (!targetStateId.IsValid ||
                targetStateId == transitionSourceStateId ||
                !visibleStates.TryGetValue(targetStateId, out CoCoStateGraphStateRecord target) ||
                HasChildren(controller.SelectedLayer, target.StateId) ||
                !CoCoStateId.TryCreate(
                    transitionSourceStateId.High,
                    transitionSourceStateId.Low,
                    out CoCoStateId sourceId) ||
                !CoCoStateId.TryCreate(targetStateId.High, targetStateId.Low, out CoCoStateId targetId))
            {
                // 维护者反馈：右键未拖拽（位移≈0）时弹卡片菜单（建子级/下钻），
                // 而不是静默取消。
                if ((endPanelPosition - transitionDragStartPanel).sqrMagnitude <= 25f)
                {
                    CardContextRequested?.Invoke(transitionSourceStateId);
                }

                return;
            }

            int priority = 0;
            foreach (CoCoStateGraphTransitionRecord transition in controller.VisibleTransitions)
            {
                if (transition.SourceStateId == transitionSourceStateId)
                {
                    priority = Mathf.Max(priority, transition.Priority + 1);
                }
            }

            controller.AddTransition(sourceId, targetId, priority, CoCoTransitionWindow.Always);
        }

        private void CancelTransitionDrag(bool releasePointer)
        {
            if (!transitionDragging)
            {
                return;
            }

            int pointerId = transitionPointerId;
            transitionDragging = false;
            if (releasePointer)
            {
                ReleaseCapturedPointer(pointerId);
            }
            else if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }

            ResetTransitionDrag();
            edgeLayer.MarkDirtyRepaint();
        }

        private void ResetTransitionDrag()
        {
            transitionPointerId = 0;
            transitionSourceStateId = default;
            transitionPointerPosition = default;
            transitionDragStartPanel = default;
        }

        private void CancelPan(bool releasePointer)
        {
            if (!panning)
            {
                return;
            }

            int pointerId = panPointerId;
            panning = false;
            SetView(panStartView, save: true);
            if (releasePointer)
            {
                ReleaseCapturedPointer(pointerId);
            }
            else if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }

            panPointerId = 0;
            panPointerStart = default;
            panStartView = default;
        }

        private void ReleaseCapturedPointer(int pointerId)
        {
            if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
        }

        private static bool Matches(CoCoSerializedId128 id, CoCoTransitionId value) =>
            value.IsValid && id.High == value.High && id.Low == value.Low;

        /// <summary>
        /// 画布 State 卡（D8：双击 Composite=下钻，等价 Animator 双击子状态机）。
        /// 拖动语义与既有交互测试逐条锚定，未改动。
        /// </summary>
        private sealed class CoCoStateGraphStateCard : VisualElement
        {
            private readonly CoCoStateGraphEditorController controller;
            private readonly CoCoStateGraphStateRecord state;
            private readonly Action<CoCoSerializedId128, Vector2, bool> moved;
            private readonly Action<CoCoSerializedId128, int, Vector2> beginTransitionDrag;
            private readonly Action<CoCoSerializedId128> cardContextRequested;
            private Vector2 position;
            private Vector2 pointerStart;
            private Vector2 positionStart;
            private int pointerId;
            private bool dragging;
            private bool hasMoved;

            internal CoCoStateGraphStateCard(
                CoCoStateGraphEditorController controller,
                CoCoStateGraphStateRecord state,
                Vector2 position,
                Action<CoCoSerializedId128, Vector2, bool> moved,
                Action<CoCoSerializedId128, int, Vector2> beginTransitionDrag,
                Action<CoCoSerializedId128> cardContextRequested)
            {
                this.controller = controller;
                this.state = state;
                this.position = position;
                this.moved = moved;
                this.beginTransitionDrag = beginTransitionDrag;
                this.cardContextRequested = cardContextRequested;
                name = "state-card";
                style.position = Position.Absolute;
                style.left = position.x;
                style.top = position.y;
                style.width = CardWidth;
                style.height = CardHeight;
                AddToClassList("state-card");
                if (IsSelected())
                {
                    AddToClassList("state-card--selected");
                }

                if (IsInitial())
                {
                    AddToClassList("state-card--initial");
                }

                tooltip = CoCoEditorLocalization.Text(
                    HasChildren()
                        ? "Double-click to open the child canvas"
                        : "Right-drag to another leaf State to add an Always Transition",
                    HasChildren()
                        ? "双击进入子级画布"
                        : "右键拖拽到另一个叶子 State 添加 Always Transition");

                var title = new Label(state.DisplayName);
                title.AddToClassList("state-card__title");
                Add(title);

                // 维护者反馈：三行——Name / State 编号（短，次小号淡色）/ Logic 名+编号（最小号）。
                // 注意：编号必须取 CoCoStateId（32 位十六进制）；CoCoSerializedId128.ToString()
                // 是类型名（首 8 字符会是 "CoCoFlow"）。
                var idLine = new Label(ShortId(ToStateId().ToString()));
                idLine.AddToClassList("state-card__id");
                Add(idLine);

                string logicName = "Unassigned";
                string logicShort = string.Empty;
                if (CoCoStateDescriptorId.TryCreate(
                        state.StateDescriptorId.High,
                        state.StateDescriptorId.Low,
                        out CoCoStateDescriptorId descriptorId) &&
                    controller.Catalog != null &&
                    controller.Catalog.TryGetStateDescriptor(descriptorId, out CoCoStateDescriptor descriptor))
                {
                    logicName = descriptor.LogicType.Name;
                    logicShort = ShortId(descriptor.DescriptorId.ToString());
                }

                var logicLine = new Label(logicShort.Length > 0 ? $"{logicName}  [{logicShort}]" : logicName);
                logicLine.AddToClassList("state-card__descriptor");
                Add(logicLine);

                if (HasChildren())
                {
                    var drill = new Button(() => controller.DrillInto(ToStateId()))
                    {
                        text = CoCoEditorLocalization.Text("Open children", "打开子级")
                    };
                    drill.AddToClassList("state-card__drill");
                    Add(drill);
                }

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                // 维护者反馈 4：右键从 State 卡拖拽 → 目标叶子 State 建 Always Transition
                // （替代已移除的左侧连接点；背景右键仍是上下文菜单）。
                // 复合卡右键（无拖拽语义）直接弹卡片菜单；叶子卡拖拽后若位移≈0 也弹菜单。
                if (evt.button == 1)
                {
                    if (!HasChildren())
                    {
                        beginTransitionDrag(state.StateId, evt.pointerId, evt.position);
                    }
                    else
                    {
                        cardContextRequested(state.StateId);
                    }

                    evt.StopPropagation();
                    return;
                }

                if (evt.button != 0 || dragging)
                {
                    return;
                }

                // D8：双击 Composite = DrillInto（Animator 子状态机行为）。
                if (evt.clickCount == 2 && HasChildren())
                {
                    controller.DrillInto(ToStateId());
                    evt.StopPropagation();
                    return;
                }

                if (!CoCoStateGraphAuthoringOperations.CanEdit(out _))
                {
                    controller.SelectState(ToStateId());
                    evt.StopPropagation();
                    return;
                }

                dragging = true;
                hasMoved = false;
                pointerId = evt.pointerId;
                pointerStart = evt.position;
                positionStart = position;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (!dragging || evt.pointerId != pointerId)
                {
                    return;
                }

                float zoom = controller.Session.GetCanvasView(
                    controller.Session.SelectedLayerId,
                    controller.Session.DrillRootStateId).Zoom;
                Vector2 delta = ((Vector2)evt.position - pointerStart) / zoom;
                if (!hasMoved && delta.sqrMagnitude <= 4f)
                {
                    return;
                }

                hasMoved = true;
                position = positionStart + delta;
                style.left = position.x;
                style.top = position.y;
                moved(state.StateId, position, false);
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!dragging || evt.pointerId != pointerId)
                {
                    return;
                }

                dragging = false;
                ReleaseCapturedPointer();
                if (hasMoved)
                {
                    moved(state.StateId, position, true);
                }

                controller.SelectState(ToStateId());
                evt.StopPropagation();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                if (!dragging || evt.pointerId != pointerId)
                {
                    return;
                }

                CancelDrag(releasePointer: true);
                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (!dragging || evt.pointerId != pointerId)
                {
                    return;
                }

                CancelDrag(releasePointer: false);
                evt.StopPropagation();
            }

            private void CancelDrag(bool releasePointer)
            {
                int capturedPointerId = pointerId;
                dragging = false;
                if (releasePointer)
                {
                    ReleaseCapturedPointer();
                }

                if (hasMoved)
                {
                    position = positionStart;
                    style.left = position.x;
                    style.top = position.y;
                    moved(state.StateId, position, false);
                }

                hasMoved = false;
                pointerId = 0;
                pointerStart = default;
                positionStart = default;
                if (!releasePointer && this.HasPointerCapture(capturedPointerId))
                {
                    this.ReleasePointer(capturedPointerId);
                }
            }

            private void ReleaseCapturedPointer()
            {
                if (this.HasPointerCapture(pointerId))
                {
                    this.ReleasePointer(pointerId);
                }
            }

            private bool IsSelected()
            {
                CoCoStateId selected = controller.Session.SelectedStateId;
                return selected.IsValid && state.StateId.High == selected.High && state.StateId.Low == selected.Low;
            }

            private bool IsInitial()
            {
                CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
                if (layer == null)
                {
                    return false;
                }

                CoCoSerializedId128 initial = state.ParentStateId.IsValid
                    ? FindState(layer, state.ParentStateId)?.InitialChildStateId ?? default
                    : layer.InitialStateId;
                return initial == state.StateId;
            }

            private bool HasChildren()
            {
                CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
                if (layer == null)
                {
                    return false;
                }

                foreach (CoCoStateGraphStateRecord candidate in layer.States)
                {
                    if (candidate != null && candidate.ParentStateId == state.StateId)
                    {
                        return true;
                    }
                }

                return false;
            }

            private CoCoStateId ToStateId()
            {
                CoCoStateId.TryCreate(state.StateId.High, state.StateId.Low, out CoCoStateId result);
                return result;
            }

            private static string ShortId(string value) =>
                string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);

            private static CoCoStateGraphStateRecord FindState(
                CoCoStateGraphLayerRecord layer,
                CoCoSerializedId128 stateId)
            {
                foreach (CoCoStateGraphStateRecord candidate in layer.States)
                {
                    if (candidate != null && candidate.StateId == stateId)
                    {
                        return candidate;
                    }
                }

                return null;
            }
        }

        private static bool HasChildren(
            CoCoStateGraphLayerRecord layer,
            CoCoSerializedId128 stateId)
        {
            if (layer == null)
            {
                return false;
            }

            foreach (CoCoStateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null && candidate.ParentStateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
