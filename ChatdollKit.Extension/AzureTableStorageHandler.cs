using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;


namespace ChatdollKit.Extension
{
    public class AzureTableStorageHandler
    {
        public string StorageURI;
        public LogType MinLevel;
        private int serialNumber = 0;
        private object locker = new object();

        public AzureTableStorageHandler(string storageURI, LogType minLevel = LogType.Warning)
        {
            StorageURI = storageURI;
            MinLevel = minLevel;
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

            // Send request
            var request = new HttpRequestMessage(HttpMethod.Post, StorageURI);
            request.Headers.Add("Accept", "application/json");
            request.Content = new StringContent(JsonConvert.SerializeObject(logJson), Encoding.UTF8, "application/json");
            using (var client = new HttpClient())
            {
                await client.SendAsync(request);
            }
        }
    }
}
