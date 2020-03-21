using UnityEngine;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif


namespace ChatdollKit.Device
{
    public class MicEnabler : MonoBehaviour
    {
        public bool IsMicrophoneEnabled { get; private set; } = false;

        private void Awake()
        {
            // Request permission for microphone
            RequestMicPermission();
        }

        private void Update()
        {
            // Update permission for microphone
            UpdateMicPermission();
        }

        // Request to use microphone
        private void RequestMicPermission()
        {
            // Androidの場合はパーミッション要求
#if PLATFORM_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.Log("Requesting permission for Mic(Android)...");
                Permission.RequestUserPermission(Permission.Microphone);
            }
#endif
        }

        // Update microphone permission
        private void UpdateMicPermission()
        {
            if (!IsMicrophoneEnabled)
            {
#if PLATFORM_ANDROID
                if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    Debug.Log("Permission for Mic is granted(Android)");
                    IsMicrophoneEnabled = true;
                }
#else
            Debug.Log("Permission for Mic is granted");
            micPermissionGranted = true;
#endif
            }
        }
    }
}
