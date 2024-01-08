using System;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;

namespace ChatdollKit.UI
{
    public class MicrophoneVolumeController : MonoBehaviour
    {
        [SerializeField]
        private Slider microphoneSlider;
        [SerializeField]
        private Image sliderHandleImage;
        [SerializeField]
        private Color32 voiceDetectedColor = new Color32(0, 204, 0, 255);
        [SerializeField]
        private Color32 voiceNotDetectedColor = new Color32(255, 255, 255, 255);

        private WakeWordListenerBase wakeWordListener;
        private IVoiceRequestProvider voiceRequestProvider;
        private DialogController dialogController;

        private Func<bool> IsWWLDetectingVoice;
        private Func<bool> IsVRPDetectingVoice;

        private void Start()
        {
            dialogController = gameObject.GetComponent<DialogController>();

            wakeWordListener = gameObject.GetComponent<WakeWordListenerBase>();
            IsWWLDetectingVoice = () => { return wakeWordListener.IsDetectingVoice; };

            voiceRequestProvider = gameObject.GetComponent<IVoiceRequestProvider>();
            if (voiceRequestProvider is VoiceRequestProviderBase)
            {
                IsVRPDetectingVoice = () => { return ((VoiceRequestProviderBase)voiceRequestProvider).IsDetectingVoice; };
            }
            else if (voiceRequestProvider is NonRecordingVoiceRequestProviderBase)
            {
                IsVRPDetectingVoice = () => { return ((NonRecordingVoiceRequestProviderBase)voiceRequestProvider).IsDetectingVoice; };
            }

            microphoneSlider.value = 1.0f - wakeWordListener.VoiceDetectionThreshold;
        }

        private void LateUpdate()
        {
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
            wakeWordListener.VoiceDetectionThreshold = 1.0f - microphoneSlider.value;
            if (voiceRequestProvider is VoiceRequestProviderBase)
            {
                ((VoiceRequestProviderBase)voiceRequestProvider).VoiceDetectionThreshold = 1.0f - microphoneSlider.value;
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
    }
}
