using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;


namespace ChatdollKit.Examples.HelloWorld
{
    public class RequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        // Dummy recognized text
        public string DummyText = "Hello, Chatdoll!";

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync
            = async (r, c, t) => { Debug.LogWarning("RequestProvider.OnStartListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync
            = async(r, c, t) => { Debug.LogWarning("RequestProvider.OnFinishListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("RequestProvider.OnErrorAsync is not implemented"); };

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token, Request preRequest = null)
        {
            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            // Listen voice
            try
            {
                // Invoke action before start recognition
                await OnStartListeningAsync(request, context, token);

                // Recognize speech
                await Task.Delay(1000); // Dummy wait
                request.Text = DummyText;
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
                await OnErrorAsync(request, context, token);
            }
            finally
            {
                // Invoke action after recognition
                await OnFinishListeningAsync(request, context, token);
            }

            return request;
        }
    }
}
