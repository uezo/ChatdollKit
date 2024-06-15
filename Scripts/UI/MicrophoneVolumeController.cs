using System;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.UI
{
    public class MicrophoneVolumeController : MonoBehaviour
    {
        [SerializeField]
        private GameObject chatdollKitObject;

        [SerializeField]
        private Slider microphoneSlider;
        [SerializeField]
        private Image sliderHandleImage;
        [SerializeField]
        private Color32 voiceDetectedColor = new Color32(0, 204, 0, 255);
        [SerializeField]
        private Color32 voiceNotDetectedColor = new Color32(255, 255, 255, 255);
        [SerializeField]
        private GameObject volumePanel;
        [SerializeField]
        private Text volumeText;

        private float volumePanelHideTimer = 0.0f;
        private float volumeUpdateInterval = 0.33f;
        private float volumeUpdateTimer = 0.0f;

        private ChatdollMicrophone microphone;
        private DialogController dialogController;
        private WakeWordListenerBase wakeWordListener;
        private IVoiceRequestProvider voiceRequestProvider;

        private Func<bool> IsWWLDetectingVoice;
        private Func<bool> IsVRPDetectingVoice;

        private void Start()
        {
            if (chatdollKitObject == null)
            {
                chatdollKitObject = FindObjectOfType<ChatdollKit>()?.gameObject;
                if (chatdollKitObject == null)
                {
                    Debug.LogError("ChatdollKit is not found in this scene.");
                }
            }

            microphone = chatdollKitObject.GetComponent<ChatdollMicrophone>();
            dialogController = chatdollKitObject.GetComponent<DialogController>();

            UpdateListeners();

            microphoneSlider.value = -1 * wakeWordListener.VoiceDetectionThreshold;
        }

        private void LateUpdate()
        {
            if (volumePanel.activeSelf)
            {
                volumePanelHideTimer += Time.deltaTime;
                if (volumePanelHideTimer >= 5.0f)
                {
                    volumePanel.SetActive(false);
                }
            }

            volumeUpdateTimer += Time.deltaTime;
            if (volumeUpdateTimer >= volumeUpdateInterval)
            {
                if (microphone.IsMuted)
                {
                    volumeText.text = $"Muted";
                }
                else
                {
                    volumeText.text = $"{microphone.CurrentVolume:f1} / {-1 * microphoneSlider.value:f1} db";
                }
                volumeUpdateTimer = 0.0f;
            }

            if (IsWWLDetectingVoice() || IsVRPDetectingVoice())
            {
                sliderHandleImage.color = voiceDetectedColor;
            }
            else
            {
                sliderHandleImage.color = voiceNotDetectedColor;
            }
        }

        public void UpdateMicrophoneSensitivity()
        {
            volumePanel.SetActive(true);
            volumePanelHideTimer = 0.0f;

            wakeWordListener.VoiceDetectionThreshold = -1 * microphoneSlider.value;
            if (voiceRequestProvider is VoiceRequestProviderBase)
            {
                ((VoiceRequestProviderBase)voiceRequestProvider).VoiceDetectionThreshold = -1 * microphoneSlider.value;
            }

            if (microphoneSlider.value == 0 && !dialogController.IsMuted)
            {
                dialogController.IsMuted = true;
            }
            else if (microphoneSlider.value > 0 && dialogController.IsMuted)
            {
                dialogController.IsMuted = false;
            }
        }

        public void UpdateListeners()
        {
            // Get WakeWordListener and VoiceRequestProvider that is currently used in DialogController

            wakeWordListener = (WakeWordListenerBase)dialogController.WakeWordListener;
            IsWWLDetectingVoice = () => { return wakeWordListener.IsDetectingVoice; };

            voiceRequestProvider = (IVoiceRequestProvider)dialogController.RequestProviders[RequestType.Voice];
            if (voiceRequestProvider is VoiceRequestProviderBase)
            {
                IsVRPDetectingVoice = () => { return ((VoiceRequestProviderBase)voiceRequestProvider).IsDetectingVoice; };
            }
            else if (voiceRequestProvider is NonRecordingVoiceRequestProviderBase)
            {
                IsVRPDetectingVoice = () => { return ((NonRecordingVoiceRequestProviderBase)voiceRequestProvider).IsDetectingVoice; };
            }
        }
    }
}
