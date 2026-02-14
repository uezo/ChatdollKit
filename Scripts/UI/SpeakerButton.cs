using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI
{
    public class SpeakerButton : MonoBehaviour
    {
        [SerializeField]
        private bool volumeMode;
        [SerializeField]
        private Image targetImage;
        [SerializeField]
        private Sprite muteSprite;
        [SerializeField]
        private Sprite unmuteSprite;
        [SerializeField]
        private GameObject volumePanel;
        [SerializeField]
        private Slider volumeSlider;
        [SerializeField]
        private AIAvatar aiAvatar;

        private const float MinDb = -80.0f;
        private const float LinearThreshold = 0.0001f;

        private void Start()
        {
            if (aiAvatar == null)
            {
                aiAvatar = FindFirstObjectByType<AIAvatar>();
                if (aiAvatar == null)
                {
                    Debug.LogWarning("AIAvatar is not found in this scene.");
                }
            }

            volumeSlider.minValue = 0.0f;
            volumeSlider.maxValue = 1.0f;
        }

        private void LateUpdate()
        {
            targetImage.sprite = aiAvatar.IsCharacterMuted ? muteSprite : unmuteSprite;
            volumeSlider.value = DbToLinear(aiAvatar.MaxCharacterVolumeDb);
        }

        public void OnButtonClick()
        {
            if (volumeMode)
            {
                volumePanel.SetActive(!volumePanel.activeSelf);
            }
            else
            {
                aiAvatar.IsCharacterMuted = !aiAvatar.IsCharacterMuted;
            }
        }

        public void OnSliderChange(float value)
        {
            var db = LinearToDb(value);

            if (aiAvatar.MaxCharacterVolumeDb > MinDb && db <= MinDb)
            {
                aiAvatar.IsCharacterMuted = true;
            }
            else if (aiAvatar.MaxCharacterVolumeDb <= MinDb && db > MinDb)
            {
                aiAvatar.IsCharacterMuted = false;
            }
            aiAvatar.MaxCharacterVolumeDb = db;
        }

        private static float LinearToDb(float linear)
        {
            return linear > LinearThreshold ? 20.0f * Mathf.Log10(linear) : MinDb;
        }

        private static float DbToLinear(float db)
        {
            return db > MinDb ? Mathf.Pow(10.0f, db / 20.0f) : 0.0f;
        }
    }
}
