using System;
using System.IO;
using UnityEngine;

namespace ChatdollKit.Demo
{
    public class FileLogger : MonoBehaviour
    {
        [SerializeField]
        private string logFilePrefix = "AITuber";
        private string logFilePath;
        private StreamWriter writer;
        
        private void Awake()
        {
            #if UNITY_EDITOR || UNITY_STANDALONE_WIN
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.productName);
            #elif UNITY_STANDALONE_OSX
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Logs", Application.productName);
            #endif

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(directory, $"{logFilePrefix}_{timestamp}.txt");

            Debug.Log($"Log file: {logFilePath}");

            writer = new StreamWriter(logFilePath, true);
            writer.AutoFlush = true;

            Application.logMessageReceived += HandleLog;
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (writer == null) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            
            var prefix = type switch
            {
                LogType.Error => "[ERROR]",
                LogType.Assert => "[ASSERT]",
                LogType.Warning => "[WARNING]",
                LogType.Log => "[INFO]",
                LogType.Exception => "[EXCEPTION]",
                _ => "[UNKNOWN]"
            };

            string message = $"{timestamp} {prefix} {logString}";
            
            if (type == LogType.Error || type == LogType.Exception)
            {
                message += $"\n{stackTrace}";
            }

            writer.WriteLine(message);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
            
            if (writer != null)
            {
                writer.Close();
                writer = null;
            }
        }
    }
}
