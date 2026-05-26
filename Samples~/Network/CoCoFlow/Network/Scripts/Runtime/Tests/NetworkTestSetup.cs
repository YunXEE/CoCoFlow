using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Tests
{
    [TestFixture]
    public abstract class NetworkTestSetup
    {
        protected GameObject TestRoot;

        [SetUp]
        public virtual void SetUp()
        {
            TestRoot = new GameObject("[Network Tests]");
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (TestRoot != null)
            {
                Object.DestroyImmediate(TestRoot);
                TestRoot = null;
            }
        }

        protected GameObject CreateTestObject(string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(TestRoot.transform);
            return obj;
        }
    }
}
