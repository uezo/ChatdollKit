using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class SimpleMessageWindow : MessageWindowBase
    {
        private Text MessageText;
        public float MessageSpeed = 0.05f;
        public float PreGap = 0.2f;
        public float PostGap = 1.0f;
        private string CurrentMessageId;

        public override void Show(string prompt = null)
        {
            SetActive(true);
            if (prompt != null)
            {
                MessageText.text = prompt;
            }
        }

        public override void Hide()
        {
            SetActive(false);
            MessageText.text = string.Empty;
        }

        public override async UniTask ShowMessageAsync(string message, CancellationToken token)
        {
            Show();
            await SetMessageAsync(message, token);
        }

        public override async UniTask SetMessageAsync(string message, CancellationToken token)
        {
            var messageId = Guid.NewGuid().ToString();
            CurrentMessageId = messageId;

            try
            {
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                await UniTask.Delay((int)(PreGap * 1000), cancellationToken: token);
                for (var i = 0; i < message.Length; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    MessageText.text = message.Substring(0, i + 1);
                    await UniTask.Delay((int)(MessageSpeed * 1000), cancellationToken: token);
                }
                MessageText.text = message;
                await UniTask.Delay((int)(PostGap * 1000), cancellationToken: token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error occured in showing message: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Do not hide when another message is begun to be shown
                if (CurrentMessageId == messageId)
                {
                    Hide();
                    MessageText.text = string.Empty;
                }
            }
        }

        private void SetActive(bool value)
        {
            if (value == true)
            {
                MessageText = gameObject.GetComponentInChildren<Text>();
            }
            gameObject.SetActive(value);
        }
    }
}
