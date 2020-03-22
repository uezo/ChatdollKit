using UnityEngine;
using ChatdollKit.Dialog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Examples
{
    // ダミーのリクエストプロバイダー
    public class DummyRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        // Dummy recognized text
        public string DummyText = string.Empty;

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync;


        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
            var request = new Request(RequestType, user);

            // Listen voice
            try
            {
                // Invoke action before start recognition
                await OnStartListeningAsync?.Invoke(request, context, token);

                // Recognize speech
                request.Text = await RecognizeOnceAsync();
                if (request.IsSet())
                {
                    Debug.Log(request.Text);
                }
                else
                {
                    Debug.LogWarning("No speech recognized");
                }
            }
            catch (TaskCanceledException)
            {
                Debug.Log("Canceled during recognizing speech");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing speech: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync?.Invoke(request, context, token);
            }
            finally
            {
                // Invoke action after recognition
                await OnFinishListeningAsync?.Invoke(request, context, token);
            }

            return request;
        }

        // Always returns the dummy text
        public async Task<string> RecognizeOnceAsync()
        {
            await Task.Delay(1000);
            return DummyText;
        }
    }
}
