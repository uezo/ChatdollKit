using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.SpeechListener;

namespace ChatdollKit.Extension.SileroVAD
{
    public class SileroVADMicrophoneButton : MonoBehaviour
    {
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
        private MicrophoneManager microphoneManager;
        [SerializeField]
        private SileroVADProcessor sileroVADProcessor;
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
            if (sileroVADProcessor == null)
            {
                sileroVADProcessor = FindFirstObjectByType<SileroVADProcessor>();
                if (sileroVADProcessor == null)
                {
                    Debug.LogWarning("SileroVADProcessor is not found in this scene.");
                }
            }
        }

        private void LateUpdate()
        {
            if (sileroVADProcessor != null && sileroVADProcessor.IsVoiceDetected)
            {
                FlashImageColor();
            }

            if (microphoneManager.IsMuted)
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
            microphoneManager.MuteMicrophone(!microphoneManager.IsMuted);
        }
    }
}
