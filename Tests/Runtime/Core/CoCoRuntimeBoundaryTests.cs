using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.Core
{
    public sealed class CoCoRuntimeBoundaryTests
    {
        [Test]
        public void LogCallsMatchTheCallingAssembliesDefineAndUnityConsole()
        {
            string marker = "P001 " + Guid.NewGuid().ToString("N");
            var events = new List<CoCoLogEvent>();
            var consoleTypes = new List<LogType>();
            int ordinaryMessageBuilds = 0;
            var agent = new CoCoEventAgent();
            agent.Subscribe((ref CoCoLogEvent entry) =>
            {
                if (entry.Message != null && entry.Message.StartsWith(marker, StringComparison.Ordinal))
                    events.Add(entry);
            });

            void OnConsole(string message, string stackTrace, LogType type)
            {
                if (message.Contains(marker))
                    consoleTypes.Add(type);
            }

            string BuildOrdinaryMessage()
            {
                ordinaryMessageBuilds++;
                return marker + " ordinary";
            }

            Application.logMessageReceived += OnConsole;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex(marker + " warning$"));
                LogAssert.Expect(LogType.Error, new Regex(marker + " error$"));
                CoCoLog.Log(BuildOrdinaryMessage());
                CoCoLog.Warning(marker + " warning");
                CoCoLog.Error(marker + " error");

#if COCOFLOW_LOG
                Assert.That(ordinaryMessageBuilds, Is.EqualTo(1));
                Assert.That(events.ConvertAll(entry => entry.Level),
                    Is.EqualTo(new[] { CoCoLogLevel.Log, CoCoLogLevel.Warning, CoCoLogLevel.Error }));
#else
                Assert.That(ordinaryMessageBuilds, Is.Zero);
                Assert.That(events.ConvertAll(entry => entry.Level),
                    Is.EqualTo(new[] { CoCoLogLevel.Warning, CoCoLogLevel.Error }));
#endif
                Assert.That(consoleTypes, Is.EqualTo(new[] { LogType.Warning, LogType.Error }));
                foreach (CoCoLogEvent entry in events)
                {
                    Assert.That(entry.ModuleName, Is.EqualTo("Core"));
                    Assert.That(entry.ClassName, Is.EqualTo(nameof(CoCoRuntimeBoundaryTests)));
                }
            }
            finally
            {
                Application.logMessageReceived -= OnConsole;
                agent.UnsubscribeAll();
            }
        }

        [Test]
        public void RegisterWaitersCanCancelAndReenterWithinTheDetachedBatch()
        {
            var initial = new CoreTestService();
            var replacement = new CoreTestService();
            var observed = new List<CoreTestService>();
            IDisposable cancelled = null;
            using var first = CoCoServices.WaitFor<CoreTestService>(service =>
            {
                observed.Add(service);
                cancelled.Dispose();
                CoCoServices.Unregister(service);
                using var nested = CoCoServices.WaitFor<CoreTestService>(observed.Add);
                CoCoServices.Register(replacement);
            });
            using var second = CoCoServices.WaitFor<CoreTestService>(_ =>
                Assert.Fail("The cancelled waiter was invoked."));
            cancelled = second;
            using var last = CoCoServices.WaitFor<CoreTestService>(observed.Add);
            try
            {
                CoCoServices.Register(initial);
                Assert.That(observed, Is.EqualTo(new[] { initial, replacement, initial }));
                Assert.That(CoCoServices.Get<CoreTestService>(), Is.SameAs(replacement));
            }
            finally
            {
                CoCoServices.Unregister(replacement);
                CoCoServices.Unregister(initial);
            }
        }

        [Test]
        public void AgentDispatchUsesPriorityAndStopsAfterUnsubscribe()
        {
            var agent = new CoCoEventAgent();
            var priorities = new List<int>();
            try
            {
                agent.Subscribe((ref float _) => priorities.Add(0));
                agent.Subscribe((ref float _) => priorities.Add(2), priority: 2);
                agent.Subscribe((ref float _) => priorities.Add(1), priority: 1);
                CoCoEventBus.Publish(1f);
                Assert.That(priorities, Is.EqualTo(new[] { 2, 1, 0 }));
            }
            finally
            {
                agent.UnsubscribeAll();
            }
            CoCoEventBus.Publish(1f);
            Assert.That(priorities, Has.Count.EqualTo(3));
        }

        [Test]
        public void DestroyedListenerIsSkippedAndLiveListenerStillReceivesTheEvent()
        {
            var first = new GameObject("Removed listener").AddComponent<CoreEventProbe>();
            var second = new GameObject("Live listener").AddComponent<CoreEventProbe>();
            CoCoEventBus.Subscribe(first);
            CoCoEventBus.Subscribe(second);
            try
            {
                Object.DestroyImmediate(first.gameObject);
                int value = 0;
                CoCoEventBus.Publish(ref value);
                Assert.That(value, Is.EqualTo(1));
            }
            finally
            {
                CoCoEventBus.Unsubscribe(first);
                CoCoEventBus.Unsubscribe(second);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Test]
        public void UnityLoggingAndServiceWaitsWorkWithoutAStateGraphHost()
        {
            LogAssert.Expect(LogType.Warning, new Regex("P003 source marker$"));
            CoCoLog.Warning("P003 source marker");
            bool cancelled = false;
            CoCoServices.WaitFor<CoreTestService>(_ => cancelled = true).Dispose();
            var service = new CoreTestService();
            CoreTestService observed = null;
            using (CoCoServices.WaitFor<CoreTestService>(value => observed = value))
            {
                try
                {
                    CoCoServices.Register(service);
                    Assert.That(observed, Is.SameAs(service));
                    Assert.That(CoCoServices.Get<CoreTestService>(), Is.SameAs(service));
                    Assert.That(cancelled, Is.False);
                }
                finally
                {
                    CoCoServices.Unregister(service);
                }
            }
            Assert.That(CoCoServices.Get<CoreTestService>(), Is.Null);
        }

        private sealed class CoreTestService { }

        [Test]
        public void LegacyMonoStateTypesDoNotExist()
        {
            var coreAssembly = typeof(CoCoServices).Assembly;
            string[] retiredTypeNames =
            {
                "CoCoStateController",
                "CoCoStateBase",
                "CoCoStateDefinition",
                "CoCoStateLayer",
                "CoCoStateChildMachine",
                "CoCoStateContextAccess",
                "CoCoStateContextDependency",
                "CoCoStateOperationDependency",
                "CoCoStateTransitionTarget",
                "CoCoStateDefinitionBuilder",
                "CoCoStateLog",
                "CoCoEventEnvelope",
                "ICoCoContext",
                "ICoCoContextProvider`1",
                "ICoCoStableEntityIdProvider",
                "CoCoLifecycleState"
            };

            foreach (string retiredTypeName in retiredTypeNames)
            {
                Assert.IsNull(
                    coreAssembly.GetType($"CoCoFlow.Runtime.Core.{retiredTypeName}"),
                    retiredTypeName);
            }
        }

    }
    internal sealed class CoreEventProbe : MonoBehaviour, ICoCoEventListener<int>
    {
        public void OnEvent(ref int value) => value++;
    }
}
