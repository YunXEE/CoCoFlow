using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

// 这个文件是 CoCoFlow 各模块记录运行信息、警告和错误的统一入口。
// 它定义日志记录的结构，并通过事件总线发布日志，供 Logger Console 订阅。
// 警告和错误还会交给 Unity 侧安装的输出端，写入 Unity 原生 Console。
namespace CoCoFlow.Runtime.Core
{
    /// <summary>表示 CoCoFlow 日志的级别。</summary>
    public enum CoCoLogLevel
    {
        Log,
        Warning,
        Error
    }

    /// <summary>携带单条日志的内容、来源及产生时间。</summary>
    public struct CoCoLogEvent
    {
        public CoCoLogLevel Level;
        public string ModuleName;
        public string ClassName;
        public string Message;
        public DateTime Timestamp;
    }

    /// <summary>在主线程发布日志事件，并转发警告和错误。</summary>
    public static class CoCoLog
    {
        #region Public API

        /// <summary>
        /// 发布普通日志事件。调用方未定义 COCOFLOW_LOG 时，
        /// 编译器会省略调用及参数求值。普通日志不写入 Unity Console。
        /// </summary>
        [Conditional("COCOFLOW_LOG")]
        public static void Log(
            string message,
            [CallerFilePath] string sourceFilePath = "")
        {
            DispatchLog(CoCoLogLevel.Log, message, sourceFilePath);
        }

        /// <summary>
        /// 发布警告日志事件，再调用已安装的警告输出端。
        /// 此方法在 COCOFLOW_LOG 开关两态下均生效。
        /// </summary>
        public static void Warning(
            string message,
            [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(CoCoLogLevel.Warning, message, info);

            WarningSink?.Invoke($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        /// <summary>
        /// 发布错误日志事件，再调用已安装的错误输出端。
        /// 此方法在 COCOFLOW_LOG 开关两态下均生效。
        /// </summary>
        public static void Error(
            string message,
            [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(CoCoLogLevel.Error, message, info);

            ErrorSink?.Invoke($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        #endregion

        #region Internal Logic

        private static readonly Dictionary<string, (string Module, string Class)> PathCache =
            new Dictionary<string, (string, string)>();

        internal static Action<string> WarningSink { get; set; }
        internal static Action<string> ErrorSink { get; set; }

        private static void DispatchLog(
            CoCoLogLevel level,
            string message,
            (string Module, string Class) info)
        {
            var logEvent = new CoCoLogEvent
            {
                Level = level,
                ModuleName = info.Module,
                ClassName = info.Class,
                Message = message,
                Timestamp = DateTime.Now
            };

            CoCoEventBus.Publish(ref logEvent);
        }

        private static void DispatchLog(
            CoCoLogLevel level,
            string message,
            string sourceFilePath)
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(level, message, info);
        }

        private static (string Module, string Class) GetFileInfo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return ("Unknown", "Unknown");
            }

            if (!PathCache.TryGetValue(filePath, out var info))
            {
                string className = Path.GetFileNameWithoutExtension(filePath);
                string directoryPath = Path.GetDirectoryName(filePath);
                string moduleName = Path.GetFileName(directoryPath);

                if (string.IsNullOrEmpty(moduleName))
                {
                    moduleName = "Global";
                }

                info = (moduleName, className);
                PathCache[filePath] = info;
            }

            return info;
        }

        #endregion
    }
}
