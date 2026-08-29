using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphEditorCanvas : VisualElement, IDisposable
    {
        private const float CanvasSize = 4096f;
        private const float CardWidth = 188f;
        private const float CardHeight = 96f;

        private readonly CoCoStateGraphEditorController controller;
        private readonly VisualElement content;
        private readonly VisualElement edgeLayer;
        private readonly Dictionary<CoCoSerializedId128, Rect> stateRects =
            new Dictionary<CoCoSerializedId128, Rect>();
        private readonly Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord> visibleStates =
            new Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord>();

        private bool panning;
        private int panPointerId;
        private Vector2 panPointerStart;
        private CoCoStateGraphCanvasView panStartView;
        private bool transitionDragging;
        private int transitionPointerId;
        private CoCoSerializedId128 transitionSourceStateId;
        private Vector2 transitionPointerPosition;

        internal CoCoStateGraphEditorCanvas(CoCoStateGraphEditorController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            name = "state-graph-canvas";
            focusable = true;
            style.flexGrow = 1f;
            style.position = Position.Relative;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.105f, 0.115f, 0.13f, 1f);

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
                    BeginTransitionDrag);
                content.Add(card);
                visibleStates[state.StateId] = state;
                stateRects[state.StateId] = new Rect(position, new Vector2(CardWidth, CardHeight));
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

        private void DrawEdges(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            painter.lineWidth = 2f;
            foreach (CoCoStateGraphTransitionRecord transition in controller.VisibleTransitions)
            {
                if (!stateRects.TryGetValue(transition.SourceStateId, out Rect source) ||
                    !stateRects.TryGetValue(transition.TargetStateId, out Rect target))
                {
                    continue;
                }

                Vector2 start = new Vector2(source.xMax, source.center.y);
                Vector2 end = new Vector2(target.xMin, target.center.y);
                if (target.center.x < source.center.x)
                {
                    start = new Vector2(source.xMin, source.center.y);
                    end = new Vector2(target.xMax, target.center.y);
                }

                bool selected = Matches(transition.TransitionId, controller.Session.SelectedTransitionId);
                painter.strokeColor = selected
                    ? new Color(1f, 0.72f, 0.2f, 1f)
                    : new Color(0.44f, 0.68f, 0.86f, 0.9f);
                painter.BeginPath();
                painter.MoveTo(start);
                painter.LineTo(end);
                painter.Stroke();

                Vector2 direction = (end - start).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x);
                Vector2 arrowBase = end - direction * 12f;
                painter.BeginPath();
                painter.MoveTo(end);
                painter.LineTo(arrowBase + normal * 5f);
                painter.MoveTo(end);
                painter.LineTo(arrowBase - normal * 5f);
                painter.Stroke();
            }

            if (transitionDragging && stateRects.TryGetValue(transitionSourceStateId, out Rect sourceRect))
            {
                painter.strokeColor = new Color(1f, 0.72f, 0.2f, 1f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(sourceRect.xMax, sourceRect.center.y));
                painter.LineTo(transitionPointerPosition);
                painter.Stroke();
            }
        }

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
            this.CapturePointer(pointerId);
            edgeLayer.MarkDirtyRepaint();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target != this && evt.target != content && evt.target != edgeLayer)
            {
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
                TryCompleteTransitionDrag();
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

        private void TryCompleteTransitionDrag()
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

        private sealed class CoCoStateGraphStateCard : VisualElement
        {
            private readonly CoCoStateGraphEditorController controller;
            private readonly CoCoStateGraphStateRecord state;
            private readonly Action<CoCoSerializedId128, Vector2, bool> moved;
            private readonly Action<CoCoSerializedId128, int, Vector2> beginTransitionDrag;
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
                Action<CoCoSerializedId128, int, Vector2> beginTransitionDrag)
            {
                this.controller = controller;
                this.state = state;
                this.position = position;
                this.moved = moved;
                this.beginTransitionDrag = beginTransitionDrag;
                name = "state-card";
                style.position = Position.Absolute;
                style.left = position.x;
                style.top = position.y;
                style.width = CardWidth;
                style.height = CardHeight;
                style.paddingLeft = 9f;
                style.paddingRight = 9f;
                style.paddingTop = 7f;
                style.paddingBottom = 7f;
                style.borderTopLeftRadius = 5f;
                style.borderTopRightRadius = 5f;
                style.borderBottomLeftRadius = 5f;
                style.borderBottomRightRadius = 5f;
                style.backgroundColor = IsSelected()
                    ? new Color(0.2f, 0.36f, 0.5f, 1f)
                    : new Color(0.16f, 0.18f, 0.21f, 1f);
                Color border = IsInitial()
                    ? new Color(0.35f, 0.86f, 0.48f, 1f)
                    : new Color(0.33f, 0.38f, 0.43f, 1f);
                style.borderLeftColor = border;
                style.borderRightColor = border;
                style.borderTopColor = border;
                style.borderBottomColor = border;
                style.borderLeftWidth = 2f;
                style.borderRightWidth = 2f;
                style.borderTopWidth = 2f;
                style.borderBottomWidth = 2f;

                var title = new Label(state.DisplayName);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 13f;
                Add(title);
                var descriptor = new Label(controller.StateDescriptorLabel(state));
                descriptor.style.fontSize = 9f;
                descriptor.style.whiteSpace = WhiteSpace.Normal;
                descriptor.style.color = new Color(0.72f, 0.76f, 0.8f, 1f);
                Add(descriptor);

                if (HasChildren())
                {
                    var drill = new Button(() => controller.DrillInto(ToStateId())) { text = "Open children" };
                    drill.style.height = 20f;
                    Add(drill);
                }
                else
                {
                    var connector = new VisualElement { name = "transition-source-connector" };
                    connector.style.position = Position.Absolute;
                    connector.style.right = -7f;
                    connector.style.top = 39f;
                    connector.style.width = 14f;
                    connector.style.height = 14f;
                    connector.style.borderTopLeftRadius = 7f;
                    connector.style.borderTopRightRadius = 7f;
                    connector.style.borderBottomLeftRadius = 7f;
                    connector.style.borderBottomRightRadius = 7f;
                    connector.style.backgroundColor = new Color(0.44f, 0.68f, 0.86f, 1f);
                    connector.tooltip = "Drag to another leaf State to add an Always Transition";
                    connector.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
                    connector.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 0)
                        {
                            beginTransitionDrag(state.StateId, evt.pointerId, evt.position);
                            evt.StopImmediatePropagation();
                        }
                    });
                    Add(connector);
                }

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || dragging)
                {
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
