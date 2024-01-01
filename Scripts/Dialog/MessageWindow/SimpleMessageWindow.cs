using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class SimpleMessageWindow : MessageWindowBase
    {
        [SerializeField]
        private Text messageText;
        public float MessageSpeed = 0.03f;
        public float PreGap = 0.1f;
        public float PostGap = 0.7f;
        [SerializeField]
        private bool autoHide = true;
        private string CurrentMessageId;

        public override void Show(string prompt = null)
        {
            SetActive(true);
            if (prompt != null)
            {
                messageText.text = prompt;
            }
        }

        public override void Hide()
        {
            try
            {
                SetActive(false);
                messageText.text = string.Empty;
            }
            catch (MissingReferenceException)
            {
                // Do nothing
            }
        }

        public override async UniTask ShowMessageAsync(string message, CancellationToken token)
        {
            Show();
            await SetMessageAsync(message, token);
        }

        public async UniTask SetMessageStreamAsync(Func<StreamMessageRequest> messageRequestGetter, CancellationToken token)
        {
            var messageId = Guid.NewGuid().ToString();
            CurrentMessageId = messageId;

            try
            {
                var messageStarted = false;
                while (!token.IsCancellationRequested)
                {
                    var messageRequest = messageRequestGetter();

                    if (messageRequest.IsEnd)
                    {
                        messageText.text = messageRequest.Text;
                        break;
                    }

                    await UniTask.Delay((int)(MessageSpeed * 1000), cancellationToken: token);

                    if (string.IsNullOrEmpty(messageRequest.Text))
                    {
                        continue;
                    }

                    if (!messageStarted)
                    {
                        messageText.text = string.Empty;    // Clear [Listening...]
                        messageStarted = true;
                        await UniTask.Delay((int)(PreGap * 1000), cancellationToken: token);
                    }

                    if (messageRequest.Text.Length > messageText.text.Length)
                    {
                        messageText.text += messageRequest.Text.Substring(messageText.text.Length, 1);
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    await UniTask.Delay((int)(PostGap * 1000), cancellationToken: token);
                    messageText.text = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore OperationCanceledException
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
                    if (autoHide)
                    {
                        Hide();
                    }
                }
            }
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
                    messageText.text = message.Substring(0, i + 1);
                    await UniTask.Delay((int)(MessageSpeed * 1000), cancellationToken: token);
                }
                messageText.text = message;
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
                    if (autoHide)
                    {
                        Hide();
                    }
                }
            }
        }

        private void SetActive(bool value)
        {
            gameObject.SetActive(value);
        }
    }

    public class StreamMessageRequest
    {
        public string Text { get; set; }
        public bool IsEnd { get; set; }
    }
}
