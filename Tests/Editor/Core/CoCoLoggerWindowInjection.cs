using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Core.Tests
{
    /// <summary>
    /// 验收注入器（方案 §2.5，D16=A）：仅存在于测试程序集，
    /// 宿主未在 manifest testables 列入本包时不编译——生产表面无此菜单。
    /// 服务 [人工] 验收 #4 的确定性触发（步骤②③⑥）。
    /// </summary>
    public static class CoCoLoggerWindowInjection
    {
        private const string MenuRoot = "CoCoFlow/Tests/Logger Console Injection";

        [MenuItem(MenuRoot + "/Standard (3 levels x5 + 3 unknown modules)")]
        private static void InjectStandard()
        {
            for (int i = 0; i < 5; i++)
            {
                Publish(CoCoLogLevel.Log, "Core", "InjectionMenu", $"standard log #{i}");
                Publish(CoCoLogLevel.Warning, "Animation", "InjectionMenu", $"standard warning #{i}");
                Publish(CoCoLogLevel.Error, "Network", "InjectionMenu", $"standard error #{i}");
            }

            for (int i = 0; i < 3; i++)
            {
                Publish(CoCoLogLevel.Log, $"UnknownModule{i}", "InjectionMenu", $"unknown module event #{i}");
            }

            Debug.Log("[CoCoLoggerWindowInjection] injected 15 level events + 3 unknown modules");
        }

        [MenuItem(MenuRoot + "/Flood (1100 events)")]
        private static void InjectFlood()
        {
            for (int i = 0; i < 1100; i++)
            {
                CoCoLogLevel level = (CoCoLogLevel)(i % 3);
                Publish(level, $"Flood{i % 7}", "InjectionMenu", $"flood event #{i}");
            }

            Debug.Log("[CoCoLoggerWindowInjection] injected 1100 flood events (exceeds MaxLogs=1000)");
        }

        private static void Publish(CoCoLogLevel level, string module, string className, string message)
        {
            var logEvent = new CoCoLogEvent
            {
                Level = level,
                ModuleName = module,
                ClassName = className,
                Message = message,
                Timestamp = System.DateTime.Now
            };
            CoCoEventBus.Publish(ref logEvent);
        }
    }
}
