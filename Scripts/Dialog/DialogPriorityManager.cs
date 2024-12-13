using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.Dialog
{
    public class DialogPriorityManager : MonoBehaviour
    {
        private DialogProcessor dialogProcessor;
        private PriorityQueue<DialogQueueItem> dialogQueue = new PriorityQueue<DialogQueueItem>();

        [SerializeField]
        private int idlingFrameThreshold = 2;
        private int idlingFrameCount = 0;

        private string textToAppendNext = string.Empty;

        private void Start()
        {
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();
        }

        private void Update()
        {
            if (dialogProcessor == null) return;

            if (dialogProcessor.Status == DialogProcessor.DialogStatus.Idling)
            {
                idlingFrameCount += 1;

                if (idlingFrameCount >= idlingFrameThreshold)
                {
                    idlingFrameCount = 0;
    
                    if (!dialogQueue.IsEmpty())
                    {
                        var dialogRequest = dialogQueue.Dequeue();
                        _ = dialogProcessor.StartDialogAsync(dialogRequest.Text, dialogRequest.Payloads);
                    }
                }
            }
            else
            {
                idlingFrameCount = 0;
            }
        }

        public void SetRequest(string text, Dictionary<string, object> payloads = null, int priority = 10)
        {
            if (priority == 0)
            {
                _ = dialogProcessor.StartDialogAsync(text + textToAppendNext);
            }
            else
            {
                dialogQueue.Enqueue(new DialogQueueItem() {
                    Priority = priority, Text = text + textToAppendNext, Payloads = payloads
                }, priority);
            }

            textToAppendNext = string.Empty;
        }

        public void SetRequestToAppendNext(string text)
        {
            textToAppendNext = "\n\n" + text;
        }

        public bool HasRequest()
        {
            return !dialogQueue.IsEmpty();
        }

        public void ClearDialogRequestQueue(int priority = 0)
        {
            dialogQueue.Clear(priority);
        }

        private class DialogQueueItem
        {
            public int Priority { get; set; }
            public string Text { get; set; }
            public Dictionary<string, object> Payloads { get; set; }
        }
    }
}
