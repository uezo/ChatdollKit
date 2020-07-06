using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;


namespace ChatdollKit.Dialog
{
    public class SimpleMessageWindow : MonoBehaviour
    {
        private Text MessageText;
        public float MessageSpeed = 0.05f;
        public float PreGap = 0.2f;
        public float PostGap = 1.0f;

        public void Show(string prompt = null)
        {
            SetActive(true);
            if (prompt != null)
            {
                ShowPrompt(prompt);
            }
        }

        public void Hide()
        {
            SetActive(false);
            MessageText.text = "";
        }

        public void ShowPrompt(string prompt)
        {
            MessageText.text = prompt;
        }

        public async Task SetMessageAsync(string message, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                await Task.Delay((int)(PreGap * 1000), token);
                for (var i = 0; i < message.Length; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    MessageText.text = message.Substring(0, i + 1);
                    await Task.Delay((int)(MessageSpeed * 1000), token);
                }
                MessageText.text = message;
                await Task.Delay((int)(PostGap * 1000), token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error occured in showing message: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Hide();
                MessageText.text = "";
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
