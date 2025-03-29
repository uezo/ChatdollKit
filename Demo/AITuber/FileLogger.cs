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

#if !UNITY_WEBGL && !UNITY_IOS
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

            writer = new StreamWriter(logFilePath, true);
            writer.AutoFlush = true;

            Application.logMessageReceived += HandleLog;

            Debug.Log($"Log file: {logFilePath}");
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

        public void OpenLogFile()
        {
            try
            {
    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logFilePath,
                    UseShellExecute = true
                });
    #elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                System.Diagnostics.Process.Start("open", $"\"{logFilePath}\"");
    #endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in opening log file: {ex.Message}");
            }
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
#endif
    }
}
