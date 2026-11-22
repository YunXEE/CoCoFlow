using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace CoCoFlow.Tests.Runtime.Core
{
    public sealed class CoCoRuntimeBoundaryTests
    {

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
                "CoCoStateDefinitionBuilder"
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
