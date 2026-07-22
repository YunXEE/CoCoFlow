using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Editor.StateGraphHost;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.StateGraphHost
{
    internal struct EditorHostIntent
    {
        public int Value;
    }

    internal struct EditorHostEvent
    {
        public int Value;
    }

    internal struct OtherEditorHostEvent
    {
        public int Value;
    }

    internal sealed class EditorHostIntentSourceComponent :
        MonoBehaviour,
        ICoCoIntentFrameSource<EditorHostIntent>
    {
        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    internal sealed class EditorHostEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<EditorHostEvent, EditorHostIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<EditorHostEvent> packet,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    internal sealed class OtherEditorHostEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<OtherEditorHostEvent, EditorHostIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<OtherEditorHostEvent> packet,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    public sealed class CoCoStateGraphHostEditorTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void CandidateDiscoveryUsesNearestHostAndDoesNotMutateSerialization()
        {
            CoCoStateGraphHost host = CreateHost("Outer");
            var validObject = CreateChild(host.transform, "Valid", false);
            var source = validObject.AddComponent<EditorHostIntentSourceComponent>();
            var adapter = validObject.AddComponent<EditorHostEventAdapterComponent>();
            var wrongAdapter = validObject.AddComponent<OtherEditorHostEventAdapterComponent>();

            var nestedObject = CreateChild(host.transform, "Nested", false);
            nestedObject.AddComponent<CoCoStateGraphHost>();
            var nestedSource = nestedObject.AddComponent<EditorHostIntentSourceComponent>();
            var nestedAdapter = nestedObject.AddComponent<EditorHostEventAdapterComponent>();

            var outsideObject = CreateObject("Outside");
            var outsideSource = outsideObject.AddComponent<EditorHostIntentSourceComponent>();
            var outsideAdapter = outsideObject.AddComponent<EditorHostEventAdapterComponent>();

            var serializedHost = new SerializedObject(host);
            int sourceSize = serializedHost.FindProperty("intentSources").arraySize;
            int adapterSize = serializedHost.FindProperty("eventAdapters").arraySize;
            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingCandidates.FindIntentSources(
                host,
                null,
                results);
            Assert.That(results, Has.Member(source));
            Assert.That(results, Has.No.Member(nestedSource));
            Assert.That(results, Has.No.Member(outsideSource));

            CoCoStateGraphHostBindingCandidates.FindEventAdapters(
                host,
                typeof(EditorHostEvent),
                typeof(EditorHostIntent),
                null,
                results);
            Assert.That(results, Has.Member(adapter));
            Assert.That(results, Has.No.Member(wrongAdapter));
            Assert.That(results, Has.No.Member(nestedAdapter));
            Assert.That(results, Has.No.Member(outsideAdapter));

            serializedHost.Update();
            Assert.That(serializedHost.FindProperty("intentSources").arraySize, Is.EqualTo(sourceSize));
            Assert.That(serializedHost.FindProperty("eventAdapters").arraySize, Is.EqualTo(adapterSize));
        }

        [Test]
        public void ExplicitBindingArraysPreserveSerializedOrder()
        {
            CoCoStateGraphHost host = CreateHost("Serialized");
            var firstObject = CreateChild(host.transform, "First", false);
            var secondObject = CreateChild(host.transform, "Second", false);
            var first = firstObject.AddComponent<EditorHostIntentSourceComponent>();
            var second = secondObject.AddComponent<EditorHostIntentSourceComponent>();
            var adapter = secondObject.AddComponent<EditorHostEventAdapterComponent>();

            var serializedHost = new SerializedObject(host);
            SerializedProperty sources = serializedHost.FindProperty("intentSources");
            sources.arraySize = 2;
            sources.GetArrayElementAtIndex(0).objectReferenceValue = second;
            sources.GetArrayElementAtIndex(1).objectReferenceValue = first;
            SerializedProperty adapters = serializedHost.FindProperty("eventAdapters");
            adapters.arraySize = 1;
            adapters.GetArrayElementAtIndex(0).objectReferenceValue = adapter;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            serializedHost = new SerializedObject(host);
            sources = serializedHost.FindProperty("intentSources");
            adapters = serializedHost.FindProperty("eventAdapters");
            Assert.That(sources.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(second));
            Assert.That(sources.GetArrayElementAtIndex(1).objectReferenceValue, Is.SameAs(first));
            Assert.That(adapters.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(adapter));
        }

        [Test]
        public void RejectedDebuggerRefreshPreservesLastCommittedSnapshot()
        {
            const ulong contentFingerprint = 0xD39UL;
            const double committedSeconds = 1.25d;
            CoCoStateGraphHost host = CreateHost("Debugger");
            CoCoStateGraphHostDebugSnapshot committed = CreateDebugSnapshot(
                contentFingerprint,
                committedSeconds);
            var view = new CoCoStateGraphHostDebuggerView();
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo snapshotField = typeof(CoCoStateGraphHostDebuggerView).GetField(
                "_snapshot",
                instancePrivate);
            FieldInfo diagnosticField = typeof(CoCoStateGraphHostDebuggerView).GetField(
                "_diagnostic",
                instancePrivate);
            MethodInfo refresh = typeof(CoCoStateGraphHostDebuggerView).GetMethod(
                "Refresh",
                instancePrivate);
            Require(snapshotField != null);
            Require(diagnosticField != null);
            Require(refresh != null);
            snapshotField.SetValue(view, committed);

            refresh.Invoke(view, new object[] { host });

            var retained = (CoCoStateGraphHostDebugSnapshot)snapshotField.GetValue(view);
            var diagnostic = (CoCoDiagnostic)diagnosticField.GetValue(view);
            Assert.That(retained, Is.SameAs(committed));
            Assert.That(retained.ContentFingerprint, Is.EqualTo(contentFingerprint));
            Assert.That(retained.Seconds, Is.EqualTo(committedSeconds));
            Assert.That(diagnostic.IsError, Is.True);
            Assert.That(
                diagnostic.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
        }

        [Test]
        public void TraceFilterBuilderKeepsStateAndTransitionIdentityModesExclusive()
        {
            Assert.That(
                CoCoStateId.TryCreate(0x11UL, 0x12UL, out CoCoStateId stateId),
                Is.True);
            Assert.That(
                CoCoTransitionId.TryCreate(
                    0x21UL,
                    0x22UL,
                    out CoCoTransitionId transitionId),
                Is.True);

            Assert.That(
                CoCoStateGraphHostDebuggerView.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.All,
                    "ignored",
                    out CoCoStateFlowTraceFilter all,
                    out string allValidation),
                Is.True);
            Assert.That(all.Kinds, Is.EqualTo(CoCoStateFlowTraceKind.All));
            Assert.That(all.StateId.IsValid, Is.False);
            Assert.That(all.TransitionId.IsValid, Is.False);
            Assert.That(allValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerView.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.StateId,
                    $"  {stateId.ToString().ToUpperInvariant()}  ",
                    out CoCoStateFlowTraceFilter state,
                    out string stateValidation),
                Is.True);
            Assert.That(state.StateId, Is.EqualTo(stateId));
            Assert.That(state.TransitionId.IsValid, Is.False);
            Assert.That(stateValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerView.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.TransitionId,
                    $"  {transitionId}  ",
                    out CoCoStateFlowTraceFilter transition,
                    out string transitionValidation),
                Is.True);
            Assert.That(transition.StateId.IsValid, Is.False);
            Assert.That(transition.TransitionId, Is.EqualTo(transitionId));
            Assert.That(transitionValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerView.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.StateId,
                    "   ",
                    out _,
                    out string blankValidation),
                Is.False);
            Assert.That(blankValidation, Is.Not.Empty);
            Assert.That(
                CoCoStateGraphHostDebuggerView.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.TransitionId,
                    "not-an-id",
                    out _,
                    out string invalidValidation),
                Is.False);
            Assert.That(invalidValidation, Is.Not.Empty);
        }

        [Test]
        public void DebuggerHostIdentityChangeResetsTraceFilter()
        {
            CoCoStateGraphHost first = CreateHost("First Debugger Host");
            CoCoStateGraphHost second = CreateHost("Second Debugger Host");
            var view = new CoCoStateGraphHostDebuggerView();
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo observeIdentity = typeof(CoCoStateGraphHostDebuggerView).GetMethod(
                "ObserveIdentity",
                instancePrivate);
            FieldInfo modeField = typeof(CoCoStateGraphHostDebuggerView).GetField(
                "_traceFilterMode",
                instancePrivate);
            FieldInfo textField = typeof(CoCoStateGraphHostDebuggerView).GetField(
                "_traceFilterText",
                instancePrivate);
            Require(observeIdentity != null);
            Require(modeField != null);
            Require(textField != null);

            observeIdentity.Invoke(view, new object[] { first });
            modeField.SetValue(view, CoCoStateGraphHostTraceFilterMode.TransitionId);
            textField.SetValue(view, "00000000000000000000000000000001");

            observeIdentity.Invoke(view, new object[] { first });
            Assert.That(
                modeField.GetValue(view),
                Is.EqualTo(CoCoStateGraphHostTraceFilterMode.TransitionId));
            Assert.That(textField.GetValue(view), Is.Not.Empty);

            observeIdentity.Invoke(view, new object[] { second });
            Assert.That(
                modeField.GetValue(view),
                Is.EqualTo(CoCoStateGraphHostTraceFilterMode.All));
            Assert.That(textField.GetValue(view), Is.EqualTo(string.Empty));
        }

        private CoCoStateGraphHost CreateHost(string name)
        {
            GameObject gameObject = CreateObject(name);
            return gameObject.AddComponent<CoCoStateGraphHost>();
        }

        private GameObject CreateChild(Transform parent, string name, bool active)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent, false);
            child.SetActive(active);
            return child;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static CoCoStateGraphHostDebugSnapshot CreateDebugSnapshot(
            ulong contentFingerprint,
            double seconds)
        {
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            ConstructorInfo snapshotConstructor =
                typeof(CoCoStateGraphHostDebugSnapshot).GetConstructors(instancePrivate)[0];
            Type graphType = snapshotConstructor.GetParameters()[0].ParameterType;
            ConstructorInfo graphConstructor = graphType.GetConstructors(instancePrivate)[0];
            object[] graphArguments = CreateDefaultArguments(graphConstructor);
            graphArguments[0] = 1U;
            graphArguments[1] = contentFingerprint;
            graphArguments[3] = 0xCA7A10UL;
            graphArguments[10] = seconds;
            object graph = graphConstructor.Invoke(graphArguments);
            object[] snapshotArguments = CreateDefaultArguments(snapshotConstructor);
            snapshotArguments[0] = graph;
            return (CoCoStateGraphHostDebugSnapshot)snapshotConstructor.Invoke(
                snapshotArguments);
        }

        private static object[] CreateDefaultArguments(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                arguments[index] = parameterType.IsArray
                    ? Array.CreateInstance(parameterType.GetElementType(), 0)
                    : parameterType.IsValueType
                        ? Activator.CreateInstance(parameterType)
                        : null;
            }

            return arguments;
        }

        private static void Require(bool condition)
        {
            Assert.That(condition, Is.True);
        }
    }
}
