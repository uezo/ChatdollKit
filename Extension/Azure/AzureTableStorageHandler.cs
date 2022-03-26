using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.Extension.Azure
{
    public class AzureTableStorageHandler
    {
        public string StorageURI;
        public LogType MinLevel;
        private int serialNumber = 0;
        private object locker = new object();
        private ChatdollHttp client;

        public AzureTableStorageHandler(string storageURI, LogType minLevel = LogType.Warning)
        {
            StorageURI = storageURI;
            MinLevel = minLevel;
            client = new ChatdollHttp();
        }

        // Log handler
        public void HandleLog(string message, string stackTrace, LogType logType)
        {
            if (logType <= MinLevel)
            {
                _ = SendLogAsync(message, stackTrace, logType);
            }
        }

        // Send log to Azure Table Service
        private async Task SendLogAsync(string message, string stackTrace, LogType logType)
        {
            // Increment the serial number to make the sequenceId unique
            lock (locker)
            {
                if (serialNumber > 999)
                {
                    serialNumber = 0;
                }
                else
                {
                    serialNumber++;
                }
            }

            // Build message
            var now = DateTime.UtcNow;
            var sequenceId = (now.Ticks).ToString() + "." + serialNumber.ToString("D4");
            var localTimestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            var logJson = new Dictionary<string, string>()
            {
                {"PartitionKey", "user1234567890"},
                {"RowKey", sequenceId.ToString()},
                {"LocalTimestamp", localTimestamp},
                {"Level", logType.ToString()},
                {"Message", message},
                {"StackTrace", stackTrace},
            };

            // Headers
            var headers = new Dictionary<string, string>() { { "Accept", "application/json" } };

            // Send request
            await client.PostJsonAsync(StorageURI, logJson, headers);
        }
    }
}
