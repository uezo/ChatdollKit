using System.Collections;
using System.Collections.Generic;
using ChatdollKit.SpeechListener;
using ChatdollKit.UI.ConversationControls;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using SpeechController = ChatdollKit.Avatar.SpeechController;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationDeviceControlsTests
    {
        private GameObject root;
        private readonly List<Object> assets = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Conversation device controls test");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
            foreach (var asset in assets) if (asset != null) Object.Destroy(asset);
            assets.Clear();
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MicrophoneMuteWorksWhileStoppedWithoutChangingThresholdOrCaptureLifecycle()
        {
            var microphone = CreateMicrophone(out var provider);
            microphone.SetNoiseGateThresholdDb(-37f);
            var control = CreateMicrophoneControl(microphone);
            yield return null;
            Assert.That(microphone.IsRecording, Is.False);
            Assert.That(control.LevelText.text, Is.EqualTo("Not recording"));
            control.MuteButton.onClick.Invoke();
            Assert.That(microphone.IsMuted, Is.True);
            Assert.That(control.LevelText.text, Is.EqualTo("Muted"));
            control.MuteButton.onClick.Invoke();
            Assert.That(microphone.IsMuted, Is.False);
            Assert.That(microphone.NoiseGateThresholdDb, Is.EqualTo(-37f));
            Assert.That(provider.StartCalls, Is.Zero);
            Assert.That(provider.StopCalls, Is.Zero);
            Assert.That(microphone.IsRecording, Is.False);
        }

        [UnityTest]
        public IEnumerator MicrophoneLevelAndExternalMuteReflectWithoutVADChanges()
        {
            var microphone = CreateMicrophone(out _);
            microphone.enabled = false;
            microphone.IsRecording = true;
            microphone.CurrentVolumeDb = -40f;
            microphone.SetNoiseGateThresholdDb(-30f);
            var control = CreateMicrophoneControl(microphone);
            var meterCallbacks = 0;
            control.LevelMeter.onValueChanged.AddListener(_ => meterCallbacks++);
            yield return null;
            Assert.That(control.LevelMeter.interactable, Is.False);
            Assert.That(control.LevelMeter.value, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(control.LevelText.text, Does.Contain("-40"));
            microphone.CurrentVolumeDb = -20f;
            yield return null;
            Assert.That(control.LevelMeter.value, Is.EqualTo(0.75f).Within(0.001f));
            microphone.MuteMicrophone(true);
            yield return null;
            Assert.That(control.Icon.sprite, Is.SameAs(control.MutedIcon));
            Assert.That(control.LevelMeter.value, Is.Zero);
            microphone.MuteMicrophone(false);
            yield return null;
            Assert.That(control.Icon.sprite, Is.SameAs(control.UnmutedIcon));
            Assert.That(control.LevelMeter.value, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(microphone.NoiseGateThresholdDb, Is.EqualTo(-30f));
            Assert.That(meterCallbacks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MicrophoneControlsKeepTargetsIndependentAndRebindSafely()
        {
            var first = CreateMicrophone(out _);
            var second = CreateMicrophone(out _);
            var firstControl = CreateMicrophoneControl(first);
            var secondControl = CreateMicrophoneControl(second);
            firstControl.MuteButton.onClick.Invoke();
            Assert.That(first.IsMuted, Is.True);
            Assert.That(second.IsMuted, Is.False);
            secondControl.MuteButton.onClick.Invoke();
            Assert.That(second.IsMuted, Is.True);
            firstControl.Microphone = second;
            yield return null;
            firstControl.MuteButton.onClick.Invoke();
            Assert.That(first.IsMuted, Is.True);
            Assert.That(second.IsMuted, Is.False);
            firstControl.Microphone = null;
            yield return null;
            Assert.That(firstControl.MuteButton.interactable, Is.False);
            Assert.That(firstControl.LevelText.text, Is.EqualTo("No microphone"));
            Assert.DoesNotThrow(() => firstControl.MuteButton.onClick.Invoke());
        }

        [UnityTest]
        public IEnumerator SpeakerUsesAssignedAudioSourceAndPreservesVolumeWhenMuting()
        {
            var speech = CreateSpeech();
            var unusedSource = speech.GetComponent<AudioSource>();
            var source = Child("Actual speech audio").AddComponent<AudioSource>();
            speech.AudioSource = source;
            source.volume = 0.35f;
            source.clip = AudioClip.Create("Silent control test", 44100, 1, 44100, false);
            assets.Add(source.clip);
            source.loop = true;
            source.Play();
            var control = CreateSpeakerControl(speech);
            yield return null;
            Assert.That(source.isPlaying, Is.True);
            control.MuteButton.onClick.Invoke();
            Assert.That(source.mute, Is.True);
            Assert.That(source.volume, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(unusedSource.mute, Is.False);
            Assert.That(source.isPlaying, Is.True);
            control.MuteButton.onClick.Invoke();
            Assert.That(source.mute, Is.False);
            Assert.That(source.volume, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(source.isPlaying, Is.True);
        }

        [UnityTest]
        public IEnumerator SpeakerZeroVolumeMuteRestoresLastPositiveVolume()
        {
            var speech = CreateSpeech();
            speech.AudioSource.volume = 0.42f;
            var control = CreateSpeakerControl(speech);
            control.VolumeSlider.value = 0f;
            Assert.That(speech.AudioSource.volume, Is.Zero);
            Assert.That(speech.AudioSource.mute, Is.True);
            control.MuteButton.onClick.Invoke();
            Assert.That(speech.AudioSource.mute, Is.False);
            Assert.That(speech.AudioSource.volume, Is.EqualTo(0.42f).Within(0.001f));
            Assert.That(control.VolumeSlider.value, Is.EqualTo(0.42f).Within(0.001f));
            control.VolumeSlider.value = 0f;
            control.VolumeSlider.value = 0.6f;
            Assert.That(speech.AudioSource.mute, Is.False);
            Assert.That(speech.AudioSource.volume, Is.EqualTo(0.6f).Within(0.001f));

            var next = CreateSpeech();
            next.AudioSource.volume = 0f;
            next.AudioSource.mute = true;
            control.Speech = next;
            yield return null;
            control.MuteButton.onClick.Invoke();
            Assert.That(next.AudioSource.mute, Is.False);
            Assert.That(next.AudioSource.volume, Is.EqualTo(1f));
            Assert.That(speech.AudioSource.volume, Is.EqualTo(0.6f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator SpeakerExternalChangesRefreshWithoutSliderFeedbackAndPanelToggles()
        {
            var speech = CreateSpeech();
            var control = CreateSpeakerControl(speech);
            var sliderCallbacks = 0;
            control.VolumeSlider.onValueChanged.AddListener(_ => sliderCallbacks++);
            speech.AudioSource.volume = 0.2f;
            speech.AudioSource.mute = true;
            yield return null;
            Assert.That(control.VolumeSlider.value, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(speech.AudioSource.mute, Is.True, "UI synchronization must not invoke the volume handler and unmute playback.");
            Assert.That(control.Icon.sprite, Is.SameAs(control.MutedIcon));
            Assert.That(sliderCallbacks, Is.Zero);
            control.SettingsButton.onClick.Invoke();
            Assert.That(control.VolumePanel.activeSelf, Is.True);
            control.SettingsButton.onClick.Invoke();
            Assert.That(control.VolumePanel.activeSelf, Is.False);
            speech.AudioSource.mute = false;
            yield return null;
            Assert.That(control.Icon.sprite, Is.SameAs(control.UnmutedIcon));
            Assert.That(sliderCallbacks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SpeakerFallsBackToSourceOnAssignedSpeechObjectAndRebinds()
        {
            var first = CreateSpeech();
            var firstAudio = first.AudioSource;
            first.AudioSource = null;
            firstAudio.volume = 0.3f;
            var second = CreateSpeech();
            second.AudioSource.volume = 0.7f;
            var control = CreateSpeakerControl(first);
            Assert.That(control.VolumeSlider.value, Is.EqualTo(0.3f).Within(0.001f));
            control.MuteButton.onClick.Invoke();
            Assert.That(firstAudio.mute, Is.True);
            Assert.That(second.AudioSource.mute, Is.False);
            control.Speech = second;
            yield return null;
            Assert.That(control.VolumeSlider.value, Is.EqualTo(0.7f).Within(0.001f));
            control.VolumeSlider.value = 0.5f;
            Assert.That(second.AudioSource.volume, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(firstAudio.volume, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(first.AudioSource, Is.Null, "Fallback lookup must not rewrite the speech configuration.");
        }

        [UnityTest]
        public IEnumerator EnableDisableDoesNotDuplicateListenersOrLeaveHandlersAttached()
        {
            var microphone = CreateMicrophone(out _);
            var speech = CreateSpeech();
            var microphoneControl = CreateMicrophoneControl(microphone);
            var speakerControl = CreateSpeakerControl(speech);
            for (var i = 0; i < 3; i++)
            {
                microphoneControl.enabled = speakerControl.enabled = false;
                microphoneControl.MuteButton.onClick.Invoke();
                speakerControl.MuteButton.onClick.Invoke();
                speakerControl.SettingsButton.onClick.Invoke();
                speakerControl.VolumeSlider.value = 0.25f;
                Assert.That(microphone.IsMuted, Is.False);
                Assert.That(speech.AudioSource.mute, Is.False);
                Assert.That(speech.AudioSource.volume, Is.EqualTo(1f));
                Assert.That(speakerControl.VolumePanel.activeSelf, Is.False);
                microphoneControl.enabled = speakerControl.enabled = true;
            }
            microphoneControl.MuteButton.onClick.Invoke();
            speakerControl.MuteButton.onClick.Invoke();
            speakerControl.SettingsButton.onClick.Invoke();
            Assert.That(microphone.IsMuted, Is.True);
            Assert.That(speech.AudioSource.mute, Is.True);
            Assert.That(speakerControl.VolumePanel.activeSelf, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MissingTargetsAndOptionalWidgetsAreSafe()
        {
            var microphone = CreateMicrophoneControl(null);
            var speaker = CreateSpeakerControl(null);
            yield return null;
            Assert.That(microphone.MuteButton.interactable, Is.False);
            Assert.That(speaker.MuteButton.interactable, Is.False);
            Assert.That(speaker.VolumeSlider.interactable, Is.False);
            Assert.That(speaker.SettingsButton.interactable, Is.False);
            Assert.DoesNotThrow(() => microphone.MuteButton.onClick.Invoke());
            Assert.DoesNotThrow(() => speaker.MuteButton.onClick.Invoke());
            Assert.DoesNotThrow(() => speaker.SettingsButton.onClick.Invoke());
            Assert.DoesNotThrow(() => speaker.VolumeSlider.onValueChanged.Invoke(0.5f));
            Child("Minimal microphone UI").AddComponent<MicrophoneControl>();
            Child("Minimal speaker UI").AddComponent<SpeakerControl>();
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private MicrophoneManager CreateMicrophone(out FakeMicrophoneProvider provider)
        {
            var host = Child("Microphone");
            host.SetActive(false);
            var microphone = host.AddComponent<MicrophoneManager>();
            microphone.AutoStart = false;
            microphone.MicrophoneProvider = provider = new FakeMicrophoneProvider();
            host.SetActive(true);
            return microphone;
        }

        private SpeechController CreateSpeech()
        {
            var speech = Child("Speech").AddComponent<SpeechController>();
            speech.AudioSource.playOnAwake = false;
            return speech;
        }

        private MicrophoneControl CreateMicrophoneControl(MicrophoneManager microphone)
        {
            var host = Child("Microphone UI");
            host.SetActive(false);
            var control = host.AddComponent<MicrophoneControl>();
            control.Microphone = microphone;
            control.MuteButton = Widget<Button>(host, "Mute");
            control.Icon = control.MuteButton.gameObject.AddComponent<Image>();
            control.MutedIcon = CreateSprite();
            control.UnmutedIcon = CreateSprite();
            control.LevelMeter = Widget<Slider>(host, "Input level");
            control.LevelText = Widget<Text>(host, "Input level text");
            host.SetActive(true);
            return control;
        }

        private SpeakerControl CreateSpeakerControl(SpeechController speech)
        {
            var host = Child("Speaker UI");
            host.SetActive(false);
            var control = host.AddComponent<SpeakerControl>();
            control.Speech = speech;
            control.MuteButton = Widget<Button>(host, "Mute");
            control.SettingsButton = Widget<Button>(host, "Settings");
            control.Icon = control.MuteButton.gameObject.AddComponent<Image>();
            control.MutedIcon = CreateSprite();
            control.UnmutedIcon = CreateSprite();
            control.VolumePanel = new GameObject("Volume panel");
            control.VolumePanel.transform.SetParent(host.transform, false);
            control.VolumeSlider = Widget<Slider>(control.VolumePanel, "Volume");
            control.VolumePanel.SetActive(false);
            host.SetActive(true);
            return control;
        }

        private static T Widget<T>(GameObject parent, string name) where T : Component
        {
            var widget = new GameObject(name, typeof(RectTransform));
            widget.transform.SetParent(parent.transform, false);
            return widget.AddComponent<T>();
        }

        private Sprite CreateSprite()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            assets.Add(sprite);
            return sprite;
        }

        private sealed class FakeMicrophoneProvider : IMicrophoneProvider
        {
            public int StartCalls;
            public int StopCalls;
            public bool IsRecording(string deviceName) => false;
            public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
            { StartCalls++; return null; }
            public void End(string deviceName) { StopCalls++; }
            public int GetPosition(string deviceName) => 0;
            public string[] devices => new[] { "Fake microphone" };
        }
    }
}
