using ChatdollKit.SpeechListener;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI.ConversationControls
{
    /// <summary>Controls microphone mute and displays the input level, independently of speech detection.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/UI/Microphone Control")]
    public sealed class MicrophoneControl : MonoBehaviour
    {
        public MicrophoneManager Microphone;
        public Button MuteButton;
        public Image Icon;
        public Sprite MutedIcon;
        public Sprite UnmutedIcon;
        [Tooltip("Optional read-only meter. Its 0–1 range represents -80–0 dB.")]
        public Slider LevelMeter;
        public Text LevelText;

        private Button boundMuteButton;

        private void OnEnable()
        {
            boundMuteButton = MuteButton;
            if (boundMuteButton != null) boundMuteButton.onClick.AddListener(ToggleMute);
            if (LevelMeter != null)
            {
                LevelMeter.minValue = 0f;
                LevelMeter.maxValue = 1f;
                LevelMeter.interactable = false;
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (boundMuteButton != null) boundMuteButton.onClick.RemoveListener(ToggleMute);
            boundMuteButton = null;
        }

        private void Update() => Refresh();

        private void ToggleMute()
        {
            if (Microphone == null) return;
            Microphone.MuteMicrophone(!Microphone.IsMuted);
            Refresh();
        }

        private void Refresh()
        {
            var available = Microphone != null;
            var muted = !available || Microphone.IsMuted;
            if (MuteButton != null) MuteButton.interactable = available;
            var sprite = muted ? MutedIcon : UnmutedIcon;
            if (Icon != null && sprite != null) Icon.sprite = sprite;

            var recording = available && Microphone.IsRecording;
            var level = recording && !muted ? Microphone.CurrentVolumeDb : -80f;
            if (float.IsNaN(level) || float.IsInfinity(level)) level = -80f;
            if (LevelMeter != null) LevelMeter.SetValueWithoutNotify(Mathf.InverseLerp(-80f, 0f, level));
            if (LevelText != null)
                LevelText.text = !available ? "No microphone" : muted ? "Muted" : !recording ? "Not recording" : $"{level:F1} dB";
        }
    }
}
