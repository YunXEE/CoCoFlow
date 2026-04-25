using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace CoCoFlow.Runtime.Core
{
    public enum LogLevel { Log, Warning, Error }

    // 使用 struct 配合你的 ref EventBus，实现零 GC 传递
    public struct CoCoLogEvent
    {
        public LogLevel Level;
        public string ModuleName;
        public string ClassName;
        public string Message;

        // 可选：记录时间戳，方便 Editor 排序
        public DateTime Timestamp;
    }

    public static class CoCoLog
    {
        private static readonly Dictionary<string, (string Module, string Class)> PathCache = new Dictionary<string, (string, string)>();

        // 【机制1】打包时只要不定义 COCOFLOW_LOG，所有调用此方法的代码都会被编译器抹除
        [Conditional("COCOFLOW_LOG")]
        public static void Log(string message, [CallerFilePath] string sourceFilePath = "")
        {
            DispatchLog(LogLevel.Log, message, sourceFilePath);
        }

        // Warning 和 Error 没有 [Conditional]，所以一定会被打包进游戏
        public static void Warning(string message, [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(LogLevel.Warning, message, info);

            UnityEngine.Debug.LogWarning($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        public static void Error(string message, [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(LogLevel.Error, message, info);

            UnityEngine.Debug.LogError($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        // 修改了一下 DispatchLog 的签名，避免重复解析路径
        private static void DispatchLog(LogLevel level, string message, (string Module, string Class) info)
        {
            var logEvent = new CoCoLogEvent
            {
                Level = level,
                ModuleName = info.Module,
                ClassName = info.Class,
                Message = message,
                Timestamp = DateTime.Now
            };

            EventBus.Publish(ref logEvent);
        }

        // 兼容 Log 调用的重载
        private static void DispatchLog(LogLevel level, string message, string sourceFilePath)
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(level, message, info);
        }

        private static (string Module, string Class) GetFileInfo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return ("Unknown", "Unknown");

            if (!PathCache.TryGetValue(filePath, out var info))
            {
                string className = Path.GetFileNameWithoutExtension(filePath);
                string directoryPath = Path.GetDirectoryName(filePath);
                string moduleName = Path.GetFileName(directoryPath);

                if (string.IsNullOrEmpty(moduleName)) moduleName = "Global";

                info = (moduleName, className);
                PathCache[filePath] = info;
            }

            return info;
        }

    }
}
