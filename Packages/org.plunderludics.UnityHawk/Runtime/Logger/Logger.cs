using UnityEngine;
using System;

namespace UnityHawk {
    public class Logger {    
        public enum LogLevel {
            Verbose,
            Info,
            Warning,
            Error
        }

        public LogLevel MinLogLevel = LogLevel.Warning;

        UnityEngine.Object _defaultContext;
        string _prefix = "[unity-hawk] "; // TODO: do we want this to be configurable?
        public Logger(UnityEngine.Object defaultContext, LogLevel minLogLevel = LogLevel.Warning) {
            MinLogLevel = minLogLevel;
            _defaultContext = defaultContext;
        }

        [HideInCallstack]
        public void Log(LogLevel level, string message, UnityEngine.Object context = null) {
            context ??= _defaultContext;
            // TODO: avoid context when logging in non-main thread?
            if (level >= MinLogLevel) {
                switch (level) {
                    case LogLevel.Warning:
                        Debug.LogWarning(_prefix + message, context);
                        break;
                    case LogLevel.Error:
                        Debug.LogError(_prefix + message, context);
                        break;
                    default:
                        Debug.Log(_prefix + message, context);
                        break;
                }
            }
        }

        // Support lazy message evaluation to avoid unnecessary string interpolation / function calls for disabled log levels
        [HideInCallstack]
        public void Log(LogLevel level, Func<string> message, UnityEngine.Object context = null) {
            if (level >= MinLogLevel) {
                Log(level, message(), context);
            }
        }
        [HideInCallstack]
        public void LogVerbose(string message, UnityEngine.Object context = null) => Log(LogLevel.Verbose, message, context);
        [HideInCallstack]
        public void Log(string message, UnityEngine.Object context = null) => Log(LogLevel.Info, message, context);
        [HideInCallstack]
        public void LogInfo(string message, UnityEngine.Object context = null) => Log(LogLevel.Info, message, context);
        [HideInCallstack]
        public void LogWarning(string message, UnityEngine.Object context = null) => Log(LogLevel.Warning, message, context);
        [HideInCallstack]
        public void LogError(string message, UnityEngine.Object context = null) => Log(LogLevel.Error, message, context);        

        [HideInCallstack]
        public void LogVerbose(Func<string> message, UnityEngine.Object context = null) => Log(LogLevel.Verbose, message, context);
        [HideInCallstack]
        public void Log(Func<string> message, UnityEngine.Object context = null) => Log(LogLevel.Info, message, context);
        [HideInCallstack]
        public void LogInfo(Func<string> message, UnityEngine.Object context = null) => Log(LogLevel.Info, message, context);
        [HideInCallstack]
        public void LogWarning(Func<string> message, UnityEngine.Object context = null) => Log(LogLevel.Warning, message, context);
        [HideInCallstack]
        public void LogError(Func<string> message, UnityEngine.Object context = null) => Log(LogLevel.Error, message, context);
    }
}
