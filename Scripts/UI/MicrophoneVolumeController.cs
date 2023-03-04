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
        private VoiceRequestProviderBase voiceRequestProvider;

        private void Start()
        {
            wakeWordListener = gameObject.GetComponent<WakeWordListenerBase>();
            voiceRequestProvider = gameObject.GetComponent<VoiceRequestProviderBase>();

            microphoneSlider.value = 1.0f - wakeWordListener.VoiceDetectionThreshold;
        }

        private void LateUpdate()
        {
            if (wakeWordListener.IsDetectingVoice || voiceRequestProvider.IsDetectingVoice)
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
            voiceRequestProvider.VoiceDetectionThreshold = 1.0f - microphoneSlider.value;
        }
    }
}
