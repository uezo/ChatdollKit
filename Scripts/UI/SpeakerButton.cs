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
        }

        private void LateUpdate()
        {
            targetImage.sprite = aiAvatar.IsCharacterMuted ? muteSprite : unmuteSprite;
            volumeSlider.value = aiAvatar.MaxCharacterVolumeDb;
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
            if (aiAvatar.MaxCharacterVolumeDb > -80.0f && value <= -80.0f)
            {
                aiAvatar.IsCharacterMuted = true;
            }
            else if (aiAvatar.MaxCharacterVolumeDb <= -80.0f && value > -80.0f)
            {
                aiAvatar.IsCharacterMuted = false;
            }
            aiAvatar.MaxCharacterVolumeDb = value;
        }
    }
}
