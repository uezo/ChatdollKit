using UnityEngine;
using UnityEngine.UI;
using SpeechController = ChatdollKit.Avatar.SpeechController;

namespace ChatdollKit.UI.ConversationControls
{
    /// <summary>Controls the assigned speech player's AudioSource without stopping its playback.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/UI/Speaker Control")]
    public sealed class SpeakerControl : MonoBehaviour
    {
        public SpeechController Speech;
        public Button MuteButton;
        public Button SettingsButton;
        public Image Icon;
        public Sprite MutedIcon;
        public Sprite UnmutedIcon;
        public Slider VolumeSlider;
        public GameObject VolumePanel;

        private Button boundMuteButton;
        private Button boundSettingsButton;
        private Slider boundVolumeSlider;
        private AudioSource observedSource;
        private float lastPositiveVolume = 1f;

        private void OnEnable()
        {
            boundMuteButton = MuteButton;
            boundSettingsButton = SettingsButton;
            boundVolumeSlider = VolumeSlider;
            if (boundMuteButton != null) boundMuteButton.onClick.AddListener(ToggleMute);
            if (boundSettingsButton != null) boundSettingsButton.onClick.AddListener(ToggleSettings);
            if (boundVolumeSlider != null)
            {
                boundVolumeSlider.minValue = 0f;
                boundVolumeSlider.maxValue = 1f;
                boundVolumeSlider.onValueChanged.AddListener(SetVolume);
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (boundMuteButton != null) boundMuteButton.onClick.RemoveListener(ToggleMute);
            if (boundSettingsButton != null) boundSettingsButton.onClick.RemoveListener(ToggleSettings);
            if (boundVolumeSlider != null) boundVolumeSlider.onValueChanged.RemoveListener(SetVolume);
            boundMuteButton = boundSettingsButton = null;
            boundVolumeSlider = null;
        }

        private void Update() => Refresh();

        private AudioSource GetAudioSource()
        {
            var source = Speech == null ? null :
                Speech.AudioSource != null ? Speech.AudioSource : Speech.GetComponent<AudioSource>();
            if (!ReferenceEquals(source, observedSource))
            {
                observedSource = source;
                lastPositiveVolume = 1f;
            }
            if (source != null && source.volume > 0f) lastPositiveVolume = source.volume;
            return source;
        }

        private void ToggleMute()
        {
            var source = GetAudioSource();
            if (source == null) return;
            var muted = source.mute || source.volume <= 0f;
            source.mute = !muted;
            if (muted && source.volume <= 0f) source.volume = lastPositiveVolume;
            Refresh();
        }

        private void ToggleSettings()
        {
            if (VolumePanel != null && GetAudioSource() != null) VolumePanel.SetActive(!VolumePanel.activeSelf);
        }

        private void SetVolume(float volume)
        {
            var source = GetAudioSource();
            if (source == null || float.IsNaN(volume) || float.IsInfinity(volume)) return;
            source.volume = Mathf.Clamp01(volume);
            source.mute = source.volume <= 0f;
            Refresh();
        }

        private void Refresh()
        {
            var source = GetAudioSource();
            var available = source != null;
            if (MuteButton != null) MuteButton.interactable = available;
            if (SettingsButton != null) SettingsButton.interactable = available && VolumePanel != null;
            if (VolumeSlider != null)
            {
                VolumeSlider.interactable = available;
                VolumeSlider.SetValueWithoutNotify(available ? source.volume : 0f);
            }
            var sprite = !available || source.mute || source.volume <= 0f ? MutedIcon : UnmutedIcon;
            if (Icon != null && sprite != null) Icon.sprite = sprite;
            if (!available && VolumePanel != null) VolumePanel.SetActive(false);
        }
    }
}
