using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;

// 标准绑定从 Logic 的 Intent 与 Operation 属性创建注入委托，在回调前刷新该步的输入。
namespace CoCoFlow.Runtime.StateGraph
{

    /// <summary>把属性声明绑定到帧内的 Intent 和写入该步 Operation 的视图。</summary>
    internal sealed class StatePropertyBindings
    {
        private readonly Action<IIntentFrame>[] _intents;

        internal static PropertyInfo[] Properties(Type logicType)
        {
            var result = new List<PropertyInfo>();
            foreach (PropertyInfo property in logicType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var operation = property.GetCustomAttribute<OperationAttribute>();
                if (operation != null && operation.HasMode)
                    throw new InvalidOperationException($"{logicType.FullName}.{property.Name} must use [Operation] without a mode.");
                if (property.IsDefined(typeof(IntentAttribute), false) || operation != null)
                {
                    result.Add(property);
                }
            }
            return result.ToArray();
        }

        internal StatePropertyBindings(StateLogic logic,
            OperationRegistry registry, StateExecutionContext context)
        {
            var intents = new List<Action<IIntentFrame>>();
            foreach (PropertyInfo property in Properties(logic.GetType()))
            {
                if (property.IsDefined(typeof(IntentAttribute), false))
                {
                    var factory = typeof(StatePropertyBindings).GetMethod(
                        nameof(CreateIntentSetter), BindingFlags.Static | BindingFlags.NonPublic);
                    intents.Add((Action<IIntentFrame>)factory.MakeGenericMethod(property.PropertyType)
                        .Invoke(null, new object[] { logic, property }));
                }
                else
                {
                    property.SetValue(logic, registry.CreateWriteView(property.PropertyType, context));
                }
            }
            _intents = intents.ToArray();
        }

        private static Action<IIntentFrame> CreateIntentSetter<TIntent>(
            StateLogic logic, PropertyInfo property)
        {
            var setter = (Action<TIntent>)Delegate.CreateDelegate(typeof(Action<TIntent>), logic, property.SetMethod);
            return frame =>
            {
                TIntent value = default;
                frame?.TryFirst(out value);
                setter(value);
            };
        }

        internal void Refresh(IIntentFrame frame)
        {
            for (int index = 0; index < _intents.Length; index++)
            {
                _intents[index](frame);
            }
        }
    }
}
