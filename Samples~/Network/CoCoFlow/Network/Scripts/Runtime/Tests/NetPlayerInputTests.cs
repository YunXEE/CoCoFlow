using System;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Addon.Network.Input;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Addon.Network.Tests
{
    [TestFixture]
    public class NetPlayerInputTests
    {
        [Test]
        public void PlayerInput_DefaultValues_AreZeroOrDefault()
        {
            var input = new NetPlayerInput();
            var fields = typeof(NetPlayerInput).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.That(fields.Length, Is.GreaterThan(0));

            foreach (var field in fields)
            {
                var value = field.GetValue(input);
                var defaultValue = Activator.CreateInstance(field.FieldType);
                Assert.That(value, Is.EqualTo(defaultValue), $"字段 {field.Name} 应使用默认值。");
            }
        }

        [Test]
        public void PlayerInput_Implements_INetworkInput()
        {
            var interfaces = typeof(NetPlayerInput).GetInterfaces();

            Assert.That(interfaces.Any(i => i.FullName == "Fusion.INetworkInput"),
                "NetPlayerInput 必须实现 Fusion.INetworkInput。");
        }

        [Test]
        public void PlayerInput_AllFields_AreValueTypes()
        {
            var fields = typeof(NetPlayerInput).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.That(fields, Is.Not.Empty);
            foreach (var field in fields)
                Assert.That(field.FieldType.IsValueType, Is.True, $"字段 {field.Name} 必须是值类型。");
        }
    }
}
