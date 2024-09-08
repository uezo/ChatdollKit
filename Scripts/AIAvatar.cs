using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Model;
using ChatdollKit.SpeechListener;

namespace ChatdollKit
{
    public class AIAvatar : MonoBehaviour
    {
        [Header("Avatar lifecycle settings")]
        [SerializeField]
        private float conversationTimeout = 10.0f;
        [SerializeField]
        private float idleTimeout = 60.0f;
        private float modeTimer = 60.0f;
        public enum AvatarMode
        {
            Disabled,
            Sleep,
            Idle,
            Conversation,
        }
        public AvatarMode Mode { get; private set; } = AvatarMode.Idle;
        private AvatarMode previousMode = AvatarMode.Idle;

        [Header("SpeechListener settings")]
        public float VoiceRecognitionThresholdDB = -50.0f;
        public float VoiceRecognitionRaisedThresholdDB = -15.0f;

        [SerializeField]
        private float conversationSilenceDurationThreshold = 0.4f;
        [SerializeField]
        private float conversationMinRecordingDuration = 0.3f;
        [SerializeField]
        private float conversationMaxRecordingDuration = 10.0f;
        [SerializeField]
        private float idleSilenceDurationThreshold = 0.3f;
        [SerializeField]
        private float idleMinRecordingDuration = 0.2f;
        [SerializeField]
        private float idleMaxRecordingDuration = 3.0f;

        public enum MicrophoneMuteStrategy
        {
            None,
            Threshold,
            Mute,
            StopDevice
        }
        public MicrophoneMuteStrategy MicrophoneMuteBy = MicrophoneMuteStrategy.Mute;

        [Header("WakeWord settings")]
        public List<WakeWord> WakeWords;
        public List<string> CancelWords;
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };
        public int WakeLength;

        [Header("ChatdollKit components")]
        public ModelController ModelController;
        public DialogProcessor DialogProcessor;
        public MicrophoneManager MicrophoneManager;
        public ISpeechListener SpeechListener;
        public MessageWindowBase UserMessageWindow;
        public MessageWindowBase CharacterMessageWindow;
 
        [Header("Error")]
        [SerializeField]
        private string ErrorVoice;
        [SerializeField]
        private string ErrorFace;
        [SerializeField]
        private string ErrorAnimationParamKey;
        [SerializeField]
        private int ErrorAnimationParamValue;

        private DialogProcessor.DialogStatus previousDialogStatus = DialogProcessor.DialogStatus.Idling;
        public Func<string, UniTask> OnWakeAsync { get; set; }
        public List<ProcessingPresentation> ProcessingPresentations = new List<ProcessingPresentation>();

        private void Awake()
        {
            // Get ChatdollKit components
            MicrophoneManager = MicrophoneManager ?? gameObject.GetComponent<MicrophoneManager>();
            ModelController = ModelController ?? gameObject.GetComponent<ModelController>();
            DialogProcessor = DialogProcessor ?? gameObject.GetComponent<DialogProcessor>();
            SpeechListener = gameObject.GetComponent<ISpeechListener>();

            // Setup MicrophoneManager
            MicrophoneManager.SetNoiseGateThresholdDb(VoiceRecognitionThresholdDB);

            // Setup ModelController
            ModelController.OnSayStart = (text, token) =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    _ = CharacterMessageWindow?.ShowMessageAsync(text, token);
                }
            };
            ModelController.OnSayEnd = () =>
            {
                CharacterMessageWindow?.Hide();
            };

            // Setup DialogProcessor
            var neutralFaceRequest = new List<FaceExpression>() { new FaceExpression("Neutral") };
            DialogProcessor.OnRequestRecievedAsync = async (text, payloads, token) =>
            {
                // Presentation
                if (ProcessingPresentations.Count > 0)
                {
                    var animAndFace = ProcessingPresentations[UnityEngine.Random.Range(0, ProcessingPresentations.Count)];
                    ModelController.StopIdling();
                    ModelController.Animate(animAndFace.Animations);
                    ModelController.SetFace(animAndFace.Faces);
                }

                // Show user message
                if (!string.IsNullOrEmpty(text))
                {
                    await UserMessageWindow.ShowMessageAsync(text, token);
                }

                // Control microphone before speech
                if (MicrophoneMuteBy == MicrophoneMuteStrategy.StopDevice)
                {
                    MicrophoneManager.StopMicrophone();
                }
                else if (MicrophoneMuteBy == MicrophoneMuteStrategy.Mute)
                {
                    MicrophoneManager.MuteMicrophone(true);
                }
                else if (MicrophoneMuteBy == MicrophoneMuteStrategy.Threshold)
                {
                    MicrophoneManager.SetNoiseGateThresholdDb(VoiceRecognitionRaisedThresholdDB);
                }

                // Restore face to neutral
                ModelController.SetFace(neutralFaceRequest);
            };

#pragma warning disable CS1998
            DialogProcessor.OnResponseShownAsync = async (response, token) =>
            {
                // Control microphone after speech
                if (MicrophoneMuteBy == MicrophoneMuteStrategy.StopDevice)
                {
                    MicrophoneManager.StartMicrophone();
                }
                else if (MicrophoneMuteBy == MicrophoneMuteStrategy.Mute)
                {
                    MicrophoneManager.MuteMicrophone(false);
                }
                else if (MicrophoneMuteBy == MicrophoneMuteStrategy.Threshold)
                {
                    MicrophoneManager.SetNoiseGateThresholdDb(VoiceRecognitionThresholdDB);
                }
            };

            DialogProcessor.OnEndAsync = async (endConversation, token) =>
            {
                if (endConversation)
                {
                    // Change to idle mode immediately
                    Mode = AvatarMode.Idle;
                    modeTimer = idleTimeout;

                    if (!token.IsCancellationRequested)
                    {
                        // NOTE: Cancel is triggered not only when just canceled but when invoked another chat session
                        // Restart idling animation and reset face expression
                        ModelController.StartIdling();
                    }
                }
            };

            DialogProcessor.OnStopAsync = async (forSuccessiveDialog) =>
            {
                // Stop speaking immediately
                ModelController.StopSpeech();

                // Start idling only when no successive dialogs are allocated
                if (!forSuccessiveDialog)
                {
                    ModelController.StartIdling();
                }
            };
#pragma warning restore CS1998

            DialogProcessor.OnErrorAsync = OnErrorAsyncDefault;

            // Setup SpeechListner
            SpeechListener.OnRecognized = OnSpeechListenerRecognized;
            SpeechListener.ChangeSessionConfig(
                silenceDurationThreshold: idleSilenceDurationThreshold,
                minRecordingDuration: idleMinRecordingDuration,
                maxRecordingDuration: idleMaxRecordingDuration
            );
        }

        private void Update()
        {
            UpdateMode();

            if (DialogProcessor.Status == DialogProcessor.DialogStatus.Idling)
            {
                if (Mode == AvatarMode.Conversation)
                {
                    if (DialogProcessor.Status != previousDialogStatus)
                    {
                        SpeechListener.ChangeSessionConfig(
                            silenceDurationThreshold: conversationSilenceDurationThreshold,
                            minRecordingDuration: conversationMinRecordingDuration,
                            maxRecordingDuration: conversationMaxRecordingDuration
                        );
                        UserMessageWindow.Show("Listening...");                        
                    }
                }
                else
                {
                    if (Mode != previousMode)
                    {
                        SpeechListener.ChangeSessionConfig(
                            silenceDurationThreshold: idleSilenceDurationThreshold,
                            minRecordingDuration: idleMinRecordingDuration,
                            maxRecordingDuration: idleMaxRecordingDuration
                        );
                        UserMessageWindow.Hide();
                    }
                }
            }

            previousDialogStatus = DialogProcessor.Status;
            previousMode = Mode;
        }

        private void UpdateMode()
        {
            if (DialogProcessor.Status != DialogProcessor.DialogStatus.Idling
                && DialogProcessor.Status != DialogProcessor.DialogStatus.Error)
            {
                Mode = AvatarMode.Conversation;
                modeTimer = conversationTimeout;
                return;
            }

            if (Mode == AvatarMode.Sleep)
            {
                return;
            }

            modeTimer -= Time.deltaTime;
            if (modeTimer > 0)
            {
                return;
            }

            if (Mode == AvatarMode.Conversation)
            {
                Mode = AvatarMode.Idle;
                modeTimer = idleTimeout;
            }
            else if (Mode == AvatarMode.Idle)
            {
                Mode = AvatarMode.Sleep;
                modeTimer = 0.0f;
            }
        }

        private string ExtractWakeWord(string text)
        {
            var textLower = text.ToLower();
            foreach (var iw in IgnoreWords)
            {
                textLower = textLower.Replace(iw.ToLower(), string.Empty);
            }

            foreach (var ww in WakeWords)
            {
                var wwText = ww.Text.ToLower();
                if (textLower.Contains(wwText))
                {
                    var prefix = textLower.Substring(0, textLower.IndexOf(wwText));
                    var suffix = textLower.Substring(textLower.IndexOf(wwText) + wwText.Length);

                    if (prefix.Length <= ww.PrefixAllowance && suffix.Length <= ww.SuffixAllowance)
                    {
                        return text;
                    }
                }
            }

            if (WakeLength > 0)
            {
                if (textLower.Length >= WakeLength)
                {
                    return text;
                }
            }

            return string.Empty;
        }

        private string ExtractCancelWord(string text)
        {
            var textLower = text.ToLower().Trim();
            foreach (var iw in IgnoreWords)
            {
                textLower = textLower.Replace(iw.ToLower(), string.Empty);
            }

            foreach (var cw in CancelWords)
            {
                if (textLower == cw.ToLower())
                {
                    return cw;
                }
            }

            return string.Empty;
        }

        public void Chat(string text = null, Dictionary<string, object> payloads = null)
        {
            if (string.IsNullOrEmpty(text.Trim()))
            {
                if (WakeWords.Count > 0)
                {
                    text = WakeWords[0].Text;
                }
                else
                {
                    Debug.LogWarning("Can't start chat without request text");
                    return;
                }
            }

            _ = DialogProcessor.StartDialogAsync(text, payloads);
        }

        public void StopChat()
        {
            DialogProcessor.StopDialog();
        }

        public void AddProcessingPresentaion(List<Model.Animation> animations, List<FaceExpression> faces)
        {
            ProcessingPresentations.Add(new ProcessingPresentation()
            {
                Animations = animations,
                Faces = faces
            });
        }

        private async UniTask OnErrorAsyncDefault(string text, Dictionary<string, object> payloads, Exception ex, CancellationToken token)
        {
            var errorAnimatedVoiceRequest = new AnimatedVoiceRequest();

            if (!string.IsNullOrEmpty(ErrorVoice))
            {
                errorAnimatedVoiceRequest.AddVoiceTTS(ErrorVoice);
            }
            if (!string.IsNullOrEmpty(ErrorFace))
            {
                errorAnimatedVoiceRequest.AddFace(ErrorFace);
            }
            if (!string.IsNullOrEmpty(ErrorAnimationParamKey))
            {
                errorAnimatedVoiceRequest.AddAnimation(ErrorAnimationParamKey, ErrorAnimationParamValue, 5.0f);
            }

            await ModelController.AnimatedSay(errorAnimatedVoiceRequest, token);
        }

        private async UniTask OnSpeechListenerRecognized(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (!string.IsNullOrEmpty(ExtractCancelWord(text)))
                {
                    DialogProcessor.StopDialog();
                    Mode = AvatarMode.Idle;
                    modeTimer = idleTimeout;
                    return;
                }
            }

            if (Mode >= AvatarMode.Conversation)
            {
                // Send text directly
                _ = DialogProcessor.StartDialogAsync(text);
            }
            else if (Mode > AvatarMode.Disabled)
            {
                // Send text if wakeword is extracted
                if (!string.IsNullOrEmpty(ExtractWakeWord(text)))
                {
                    if (OnWakeAsync != null)
                    {
                        await OnWakeAsync(text);
                    }
                    _ = DialogProcessor.StartDialogAsync(text);
                }
            }
        }

        public void ChangeSpeechListener(ISpeechListener speechListener)
        {
            SpeechListener.StopListening();
            SpeechListener = speechListener;
            SpeechListener.OnRecognized = OnSpeechListenerRecognized;
        }

        public class ProcessingPresentation
        {
            public List<Model.Animation> Animations { get; set; } = new List<Model.Animation>();
            public List<FaceExpression> Faces { get; set; }
        }
    }
}
