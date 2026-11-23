// 生成带来源和级别的日志事件，并按级别交给已安装的输出端。

using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>日志的严重级别。</summary>
    public enum CoCoLogLevel
    {
        Log,
        Warning,
        Error
    }

    /// <summary>供日志监控订阅者读取的单条日志。</summary>
    public struct CoCoLogEvent
    {
        public CoCoLogLevel Level;
        public string ModuleName;
        public string ClassName;
        public string Message;
        public DateTime Timestamp;
    }

    /// <summary>发布日志事件，并将警告和错误转交给已安装的输出端。</summary>
    public static class CoCoLog
    {
        #region Public API

        /// <summary>发布普通日志。调用程序集未定义 COCOFLOW_LOG 时，调用及参数求值会被编译器移除。</summary>
        /// <param name="message">要输出的日志文本。</param>
        /// <param name="sourceFilePath">用于识别来源的文件路径，默认取调用处的源文件路径。</param>
        [Conditional("COCOFLOW_LOG")]
        public static void Log(string message, [CallerFilePath] string sourceFilePath = "")
        {
            DispatchLog(CoCoLogLevel.Log, message, sourceFilePath);
        }

        /// <summary>发布警告事件，随后转发给已安装的警告输出端。</summary>
        /// <param name="message">要输出的日志文本。</param>
        /// <param name="sourceFilePath">用于识别来源的文件路径，默认取调用处的源文件路径。</param>
        public static void Warning(string message, [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(CoCoLogLevel.Warning, message, info);

            WarningSink?.Invoke($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        /// <summary>发布错误事件，随后转发给已安装的错误输出端。</summary>
        /// <param name="message">要输出的日志文本。</param>
        /// <param name="sourceFilePath">用于识别来源的文件路径，默认取调用处的源文件路径。</param>
        public static void Error(string message, [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(CoCoLogLevel.Error, message, info);

            ErrorSink?.Invoke($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        #endregion

        #region Internal Logic

        private static readonly Dictionary<string, (string Module, string Class)> PathCache = new Dictionary<string, (string, string)>();

        internal static Action<string> WarningSink { get; set; }
        internal static Action<string> ErrorSink { get; set; }

        private static void DispatchLog(CoCoLogLevel level, string message, (string Module, string Class) info)
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

        private static void DispatchLog(CoCoLogLevel level, string message, string sourceFilePath)
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

        #endregion
    }
}
