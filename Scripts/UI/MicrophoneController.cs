using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.SpeechListener;

namespace ChatdollKit.UI
{
    public class MicrophoneController : MonoBehaviour
    {
        [SerializeField]
        private MicrophoneManager microphoneManager;

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
        private float previousVolume = -99.9f;

        private void Start()
        {
            if (microphoneManager == null)
            {
                microphoneManager = FindFirstObjectByType<MicrophoneManager>();
                if (microphoneManager == null)
                {
                    Debug.LogWarning("MicrophoneManager is not found in this scene.");
                }
            }

            microphoneSlider.value = -1 * microphoneManager.NoiseGateThresholdDb;
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
                if (microphoneManager.IsMuted)
                {
                    volumeText.text = $"Muted";
                }
                else
                {
                    var volumeToShow = microphoneManager.CurrentVolumeDb > -99.9f ? microphoneManager.CurrentVolumeDb : previousVolume;
                    volumeText.text = $"{volumeToShow:f1} / {-1 * microphoneSlider.value:f1} db";
                }
                volumeUpdateTimer = 0.0f;
            }
            if (microphoneManager.CurrentVolumeDb > -99.9f)
            {
                previousVolume = microphoneManager.CurrentVolumeDb;
            }

            if (microphoneManager.CurrentVolumeDb > microphoneManager.NoiseGateThresholdDb)
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

            microphoneManager.SetNoiseGateThresholdDb(-1 * microphoneSlider.value);
            if (microphoneSlider.value == 0 && !microphoneManager.IsMuted)
            {
                microphoneManager.MuteMicrophone(true);
            }
            else if (microphoneSlider.value > 0 && microphoneManager.IsMuted)
            {
                microphoneManager.MuteMicrophone(false);
            }
        }
    }
}
