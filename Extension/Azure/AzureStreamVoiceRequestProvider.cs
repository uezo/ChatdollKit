using System;
using System.Threading;
using UnityEngine;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Extension.Azure
{
    public class AzureStreamVoiceRequestProvider : VoiceRequestProviderBase
    {
        [Header("Azure Settings")]
        public string ApiKey;
        public string Region;
        public string Language = "ja-JP";

        private AudioConfig audioConfig;
        private SpeechConfig speechConfig;
        private SpeechRecognizer recognizer;
        private string recognizedTextBuffer;
        private bool isRecognizing;

        public async UniTask StartRecognitionAsync()
        {
            // Initialize recognizer
            audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            speechConfig = SpeechConfig.FromSubscription(ApiKey, Region);
            if (!string.IsNullOrEmpty(Language))
            {
                speechConfig.SpeechRecognitionLanguage = Language;
            }
            speechConfig.SetProperty(
                PropertyId.Speech_SegmentationSilenceTimeoutMs,
                ((int)(SilenceDurationToEndRecording * 1000)).ToString()
            );
            speechConfig.SetProperty(
                PropertyId.Conversation_Initial_Silence_Timeout,
                ((int)(ListeningTimeout * 1000)).ToString()
            );

            recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            recognizer.Recognizing += (sender, e) =>
            {
                recognizedTextBuffer = e.Result.Text;
            };
            recognizer.Recognized += (sender, e) =>
            {
                _ = StopRecognitionAsync();
                recognizedTextBuffer = e.Result.Text;
                if (PrintResult)
                {
                    Debug.Log($"Recognized(VoiceRequestProvider): {recognizedTextBuffer}");
                }
            };

            // Start recognizing
            recognizedTextBuffer = string.Empty;
            await recognizer.StartContinuousRecognitionAsync();
            isRecognizing = true;

            Debug.Log("Speech recognition started");
        }

        public async UniTask StopRecognitionAsync()
        {
            if (recognizer != null)
            {
                try
                {
                    await recognizer.StopContinuousRecognitionAsync();
                    recognizer.Dispose();
                    Debug.Log("Speech recognition stopped");
                }
                catch (ObjectDisposedException)
                {
                    // Do nothing
                }
            }

            isRecognizing = false;
        }

        public override async UniTask<Request> GetRequestAsync(CancellationToken token)
        {
            var request = new Request(RequestType);

            try
            {
                // Update RecognitionId
                var currentRecognitionId = Guid.NewGuid().ToString();
                recognitionId = currentRecognitionId;

                // Invoke action before start recognition
                await (OnStartListeningAsync ?? OnStartListeningDefaultAsync).Invoke(request, token);

                // Start
                await StartRecognitionAsync();
                _ = ((SimpleMessageWindow)MessageWindow).SetMessageStreamAsync(() => {
                    return new StreamMessageRequest() { Text = recognizedTextBuffer, IsEnd = !isRecognizing };
                }, token);

                while (isRecognizing)
                {
                    if (!string.IsNullOrEmpty(TextInput))
                    {
                        await StopRecognitionAsync();
                        recognizedTextBuffer = TextInput;
                        TextInput = string.Empty;
                    }

                    await UniTask.Delay(50, cancellationToken: token);
                }

                // Exit if RecognitionId is updated by another request
                if (recognitionId != currentRecognitionId)
                {
                    Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                }
                else
                {
                    if (!string.IsNullOrEmpty(recognizedTextBuffer))
                    {
                        request.Text = recognizedTextBuffer;
                        if (PrintResult)
                        {
                            Debug.Log($"Recognized(VoiceRequestProvider): {request.Text}");
                        }
                    }
                }

                if (!request.IsSet())
                {
                    Debug.LogWarning("No speech recognized");
                }

                // Clean up to check cancel
                var text = request.Text;
                foreach (var iw in IgnoreWords)
                {
                    text = text.Replace(iw, string.Empty);
                }

                // Check cancellation
                if (CancelWords.Contains(text))
                {
                    Debug.LogWarning("Request canceled");
                    request.IsCanceled = true;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Canceled during recognizing speech");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing speech: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync(request, token);
            }
            finally
            {
                StopListening();
                // Invoke action after recognition
                await (OnFinishListeningAsync ?? OnFinishListeningDefaultAsync).Invoke(request, token);
            }

            return request;
        }

#pragma warning disable CS1998
        protected override async UniTask OnFinishListeningDefaultAsync(Request request, CancellationToken token)
        {
            // Do nothing (In default settings, the entire recognized text is displayed.)
        }
#pragma warning restore CS1998
    }

}
