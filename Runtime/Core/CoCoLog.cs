using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Indicates the severity of a log event.</summary>
    public enum CoCoLogLevel
    {
        /// <summary>Ordinary diagnostic output.</summary>
        Log,
        /// <summary>A warning reported by the caller.</summary>
        Warning,
        /// <summary>An error reported by the caller.</summary>
        Error
    }

    /// <summary>Carries a log entry to synchronous monitoring subscribers.</summary>
    public struct CoCoLogEvent
    {
        /// <summary>The severity assigned by the logging entry point.</summary>
        public CoCoLogLevel Level;
        /// <summary>The name of the source file's enclosing directory.</summary>
        public string ModuleName;
        /// <summary>The source file name without its extension.</summary>
        public string ClassName;
        /// <summary>The message supplied by the caller.</summary>
        public string Message;
        /// <summary>The local time when the entry was published.</summary>
        public DateTime Timestamp;
    }

    /// <summary>Publishes log events and forwards warnings and errors to installed sinks.</summary>
    public static class CoCoLog
    {
        #region Public API

        /// <summary>Publishes an ordinary log event. The call and its arguments are omitted when COCOFLOW_LOG is undefined in the calling compilation.</summary>
        /// <param name="message">The text to publish.</param>
        /// <param name="sourceFilePath">The source used to identify the module and file. Defaults to the caller path.</param>
        [Conditional("COCOFLOW_LOG")]
        public static void Log(string message, [CallerFilePath] string sourceFilePath = "")
        {
            DispatchLog(CoCoLogLevel.Log, message, sourceFilePath);
        }

        /// <summary>Publishes a warning event, then forwards formatted text to the installed warning sink if available.</summary>
        /// <param name="message">The text to publish.</param>
        /// <param name="sourceFilePath">The source used to identify the module and file. Defaults to the caller path.</param>
        public static void Warning(string message, [CallerFilePath] string sourceFilePath = "")
        {
            var info = GetFileInfo(sourceFilePath);
            DispatchLog(CoCoLogLevel.Warning, message, info);

            WarningSink?.Invoke($"[CoCoFlow: {info.Module}]{info.Class}: {message}");
        }

        /// <summary>Publishes an error event, then forwards formatted text to the installed error sink if available.</summary>
        /// <param name="message">The text to publish.</param>
        /// <param name="sourceFilePath">The source used to identify the module and file. Defaults to the caller path.</param>
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
