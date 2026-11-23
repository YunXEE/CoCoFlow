using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Tests.Runtime.Input
{
    public sealed class InputReaderBoundaryTests
    {
        [Test]
        public void InputReaderIsTheOnlyInputAuthorityWithoutCoreBridge()
        {
            var inputReaderType = typeof(InputReader);
            var inputAssembly = inputReaderType.Assembly;
            var coreAssembly = typeof(CoCoServices).Assembly;

            Assert.IsTrue(inputReaderType.IsSealed);
            RequireComponent requirement = inputReaderType
                .GetCustomAttributes(typeof(RequireComponent), false)
                .Cast<RequireComponent>()
                .Single();
            Assert.AreSame(typeof(PlayerInput), requirement.m_Type0);
            Assert.IsNull(inputAssembly.GetType(
                "CoCoFlow.Runtime.Modules.Input.InputRuntime"));
            Assert.IsNull(inputAssembly.GetType(
                "CoCoFlow.Runtime.Modules.Input.InputMapType"));

            string[] retiredCoreInputTypes =
            {
                "CoCoInputIntent",
                "IInputStateProvider",
                "IInputEventSource",
                "IInputModeController",
                "InputMapNames"
            };
            foreach (string retiredTypeName in retiredCoreInputTypes)
            {
                Assert.IsNull(coreAssembly.GetType(
                    $"CoCoFlow.Runtime.Core.{retiredTypeName}"),
                    retiredTypeName);
            }
        }
    }
}
