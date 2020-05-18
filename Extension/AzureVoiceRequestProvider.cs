using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
//using Microsoft.CognitiveServices.Speech;
using ChatdollKit.Dialog;


namespace ChatdollKit.Extension
{
    public class AzureVoiceRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        // Azure configurations
        public string ApiKey;
        public string Region;
        public string Language;

        // Dummy for test
        public bool UseDummy = false;
        public string DummyText = string.Empty;

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync;


        private void Start()
        {
            // Don't remove this method to be able to inactivate this provider
        }

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

        // Speech to Text by Azure
        public async Task<string> RecognizeOnceAsync()
        {
            // For debugging and testing
            if (UseDummy)
            {
                await Task.Delay(1000);
                return DummyText;
            }

            // Declare return value
            var recognizedText = string.Empty;

            //// Configure Azure STT
            //var config = SpeechConfig.FromSubscription(ApiKey, Region);
            //config.SpeechRecognitionLanguage = Language;

            //// Call speech recognizer
            //using (var recognizer = new SpeechRecognizer(config))
            //{
            //    var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(false);

            //    // Checks result
            //    if (result.Reason == ResultReason.RecognizedSpeech)
            //    {
            //        // Successfully recognized
            //        recognizedText = result.Text;
            //    }
            //    else if (result.Reason == ResultReason.NoMatch)
            //    {
            //        // Nothing recognized
            //        Debug.Log("No speech recognized");
            //    }
            //    else if (result.Reason == ResultReason.Canceled)
            //    {
            //        // Canceled because error
            //        var cancellation = CancellationDetails.FromResult(result);
            //        Debug.LogError($"Speech recognition failed: Reason={cancellation.Reason} Details={cancellation.ErrorDetails}");
            //    }
            //    else
            //    {
            //        // Unknown error
            //        Debug.LogError($"Unknown error in speech recognition: {result.Reason.ToString()}");
            //    }
            //}
            return recognizedText;
        }
    }
}
