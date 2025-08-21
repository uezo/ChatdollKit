using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.SpeechListener;

namespace ChatdollKit.UI
{
    public class MicrophoneButton : MonoBehaviour
    {
        [SerializeField]
        private bool volumeMode;
        [SerializeField]
        private Image targetImage;
        [SerializeField]
        private Color originalColor = Color.white;
        [SerializeField]
        private Color flashColor = new Color32(0, 204, 0, 255);
        [SerializeField]
        private float flashDuration = 0.05f;
        [SerializeField]
        private float revertDuration = 0.2f;
        [SerializeField]
        private Sprite muteSprite;
        [SerializeField]
        private Sprite unmuteSprite;
        [SerializeField]
        private GameObject volumePanel;
        [SerializeField]
        private Slider volumeSlider;
        [SerializeField]
        private MicrophoneManager microphoneManager;
        [SerializeField]
        private SpeechListenerBase speechListener;
        [SerializeField]
        private Coroutine flashCoroutine;

        private void Start()
        {
            targetImage.color = originalColor;

            if (microphoneManager == null)
            {
                microphoneManager = FindFirstObjectByType<MicrophoneManager>();
                if (microphoneManager == null)
                {
                    Debug.LogWarning("MicrophoneManager is not found in this scene.");
                }
            }
            if (speechListener == null)
            {
                speechListener = FindFirstObjectByType<SpeechListenerBase>();
                if (speechListener == null)
                {
                    Debug.LogWarning("SpeechListener is not found in this scene.");
                }
            }

            volumeSlider.value = microphoneManager.NoiseGateThresholdDb * -1;
        }

        private void LateUpdate()
        {
            volumeSlider.value = microphoneManager.NoiseGateThresholdDb * -1;

            if (speechListener.IsVoiceDetected)
            {
                FlashImageColor();
            }

            if (microphoneManager.IsMuted || volumeSlider.value == 0)
            {
                targetImage.sprite = muteSprite;
                if (flashCoroutine != null)
                {
                    flashCoroutine = StartCoroutine(RevertColorCoroutine());
                }
            }
            else
            {
                targetImage.sprite = unmuteSprite;
            }
        }

        public void FlashImageColor()
        {
            if (flashCoroutine != null)
            {
                StopCoroutine(flashCoroutine);
            }
            flashCoroutine = StartCoroutine(FlashColorCoroutine());
        }

        private IEnumerator FlashColorCoroutine()
        {
            targetImage.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            flashCoroutine = StartCoroutine(RevertColorCoroutine());
        }

        private IEnumerator RevertColorCoroutine()
        {
            float elapsedTime = 0f;
            Color currentColor = targetImage.color;
            while (elapsedTime < revertDuration)
            {
                targetImage.color = Color.Lerp(currentColor, originalColor, elapsedTime / revertDuration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            targetImage.color = originalColor;
            flashCoroutine = null;
        }

        public void OnButtonClick()
        {
            if (volumeMode)
            {
                volumePanel.SetActive(!volumePanel.activeSelf);
            }
            else
            {
                microphoneManager.MuteMicrophone(!microphoneManager.IsMuted);
            }
        }

        public void OnSliderChange(float value)
        {
            if (value == 0 && !microphoneManager.IsMuted)
            {
                microphoneManager.MuteMicrophone(true);
            }
            else if (value > 0 && microphoneManager.IsMuted)
            {
                microphoneManager.MuteMicrophone(false);
            }
            microphoneManager.SetNoiseGateThresholdDb(value * -1);
        }
    }
}
