using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Modules.Network.Tests
{
    /// <summary>
    /// 验证 NetPlayerInput 结构体的纯 C# 属性。
    /// 使用反射避免对 Fusion 运行时类型的直接依赖。
    /// </summary>
    [TestFixture]
    public class NetPlayerInputTests
    {
        [Test]
        public void PlayerInput_DefaultValues_AreZeroOrDefault()
        {
            var input = new NetPlayerInput();
            var fields = typeof(NetPlayerInput).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.That(fields.Length, Is.GreaterThan(0), "Struct should have at least one field");

            foreach (var field in fields)
            {
                var value = field.GetValue(input);
                var defaultValue = Activator.CreateInstance(field.FieldType);
                Assert.That(value, Is.EqualTo(defaultValue),
                    $"Field '{field.Name}' should have default value when struct is default-initialized");
            }
        }

        [Test]
        public void PlayerInput_Implements_INetworkInput()
        {
            var interfaces = typeof(NetPlayerInput).GetInterfaces();
            Assert.That(interfaces.Any(i => i.Name == "INetworkInput"),
                "NetPlayerInput must implement INetworkInput marker interface for Fusion input system");
        }

        [Test]
        public void PlayerInput_AllFields_AreValueTypes()
        {
            var fields = typeof(NetPlayerInput).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.That(fields, Is.Not.Empty, "Struct should have fields");

            foreach (var field in fields)
            {
                Assert.That(field.FieldType.IsValueType, Is.True,
                    $"Field '{field.Name}' of type '{field.FieldType.Name}' must be a value type. " +
                    "Network input fields should not be reference types to avoid GC allocation.");
            }
        }
    }
}
