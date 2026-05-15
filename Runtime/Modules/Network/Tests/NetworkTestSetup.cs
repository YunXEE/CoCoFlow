using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Runtime.Modules.Network.Tests
{
    /// <summary>
    /// 网络模块测试基类。所有 Network 单元测试都应继承此类。
    /// </summary>
    [TestFixture]
    public abstract class NetworkTestSetup
    {
        protected GameObject _testRoot;

        [SetUp]
        public virtual void SetUp()
        {
            _testRoot = new GameObject("[Network Tests]");
            Object.DontDestroyOnLoad(_testRoot);
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (_testRoot != null)
            {
                Object.DestroyImmediate(_testRoot);
                _testRoot = null;
            }
        }

        #region Public API

        protected GameObject CreateTestObject(string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(_testRoot.transform);
            return obj;
        }

        protected IEnumerator WaitForFrames(int frameCount)
        {
            for (var i = 0; i < frameCount; i++)
                yield return null;
        }

        protected IEnumerator AwaitUniTask(UniTask task)
        {
            var asyncOp = task.ToCoroutine();
            yield return asyncOp;
        }

        #endregion

        #region Internal Logic

        protected virtual void ConfigureMockServices()
        {
            // 子类覆写以注册模拟服务，例如:
            // CoCoServices.Register<INetworkRunnerProvider>(new MockNetworkRunnerProvider());
        }

        #endregion
    }
}
