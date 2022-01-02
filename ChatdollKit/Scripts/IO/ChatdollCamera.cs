using System;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
using Cysharp.Threading.Tasks;

namespace ChatdollKit.IO
{
    public class ChatdollCamera : MonoBehaviour
    {
        // Permission
        public bool IsCameraEnabled { get; private set; } = false;

        // Camera settings
        [Header("Camera Settings")]
        public Vector2Int Size = new Vector2Int(640, 480);
        public int Fps = 10;
        public float PreviewTime = 2.0f;
        public float LaunchTimeout = 10.0f;
        public AudioClip CaptureSound;

        // Preview show/hide control
        public Action<GameObject, GameObject> ShowPreview;
        public Action<GameObject, GameObject> HidePreview;
        // Action called when captured photo / self-timer decremented
        public Action OnCaptured;
        public Action<int> OnTimer;

        // Camera status
        public bool IsAlreadyLaunched { get; private set; }

        // Code reader settings
        [Header("Code Reader Settings")]
        public string CodeReaderCaption = "Scan Code";
        public int CodeReaderFps = 10;
        public float ReadTimeout = 60.0f;

        // Function to decode
        public Func<Texture2D, string> DecodeCode;
        // Action called when decoded successfully
        public Action<string> OnCodeRead;

        // Components
        private WebCamTexture webCamTexture;
        private RawImage previewWindow;
        private GameObject backgroundPanel;
        private Text caption;
        private Text selfTimerCounter;
        private AudioSource audioSource;

        private void Awake()
        {
#if PLATFORM_ANDROID
            // Request permission if Android
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
            }
#endif

            backgroundPanel = gameObject.GetComponentInChildren<Image>(true)?.gameObject;
            if (backgroundPanel == null)
            {
                Debug.LogWarning("BackgroundPanel is not found");
            }

            previewWindow = gameObject.GetComponentInChildren<RawImage>(true);
            if (previewWindow == null)
            {
                Debug.LogWarning("PreviewWindow is not found");
            }

            foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
            {
                if (text.name == "Caption")
                {
                    caption = text;
                }
                else if (text.name == "SelfTimerCounter")
                {
                    selfTimerCounter = text;
                }
            }
            if (caption == null)
            {
                Debug.LogWarning("Caption is not found");
            }
            if (selfTimerCounter == null)
            {
                Debug.LogWarning("SelfTimerCounter is not found");
            }

            audioSource = gameObject.GetComponentInChildren<AudioSource>(true);
            if (audioSource == null)
            {
                Debug.LogWarning("AudioSource is not found");
            }
            else if (CaptureSound != null)
            {
                audioSource.clip = CaptureSound;
            }
        }

        private void Update()
        {
            if (!IsCameraEnabled)
            {
#if PLATFORM_ANDROID
                // Check permission if Android
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    IsCameraEnabled = true;
                }
                else
                {
                    return;
                }
#else
                IsCameraEnabled = true;
#endif
            }
        }

        private void OnDestroy()
        {
            Close();
        }

        public bool Launch(string caption = null, int fps = 0)
        {
            if (IsAlreadyLaunched)
            {
                return false;
            }

            IsAlreadyLaunched = true;

            try
            {
                // Configure camera and launch
                webCamTexture = new WebCamTexture(Size.x, Size.y, fps > 0 ? fps : Fps);
                previewWindow.texture = webCamTexture;
                webCamTexture.Play();

                // Set caption
                this.caption.text = caption ?? string.Empty;

                // Show preview
                (ShowPreview ?? ShowPreviewDefault).Invoke(previewWindow.gameObject, backgroundPanel);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in starting camera: {ex.Message}\n{ex.StackTrace}");
                webCamTexture?.Stop();
            }

            return false;
        }

        public void Close()
        {
            (HidePreview ?? HidePreviewDefault).Invoke(previewWindow.gameObject, backgroundPanel);
            webCamTexture?.Stop();
            IsAlreadyLaunched = false;
        }

        public void ShowPreviewDefault(GameObject preview, GameObject background)
        {
            background.SetActive(true);
            preview.SetActive(true);
        }

        public void HidePreviewDefault(GameObject preview, GameObject background)
        {
            preview.SetActive(false);
            background.SetActive(false);
        }

        public Texture2D GetTexture()
        {
            var photo = new Texture2D(webCamTexture.width, webCamTexture.height);
            photo.SetPixels32(webCamTexture.GetPixels32());
            photo.Apply();
            return photo;
        }

        public byte[] Capture()
        {
            return CaptureAsTexture().EncodeToJPG();
        }

        public Texture2D CaptureAsTexture()
        {
            if (!IsReadyToCapture())
            {
                throw new Exception("Camera is not ready to capture");
            }

            // Get texture from WebCamTexture
            var photo = GetTexture();

            audioSource.Play();
            OnCaptured?.Invoke();

            // Return texture
            return photo;
        }

        public async UniTask CaptureAsync(string path)
        {
            if (!IsReadyToCapture())
            {
                throw new Exception("Camera is not ready to capture");
            }

            // Get texture from WebCamTexture
            var photo = GetTexture();
            if (PreviewTime > 0)
            {
                // Preview captured photo when preview time is longer than zero
                previewWindow.texture = photo;
            }
            var waitTask = UniTask.Delay((int)(PreviewTime * 1000));

            // Save as file
            var img = photo.EncodeToJPG();
            var st = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
            var writeTask = st.WriteAsync(img, 0, img.Length).AsUniTask();

            audioSource.Play();
            OnCaptured?.Invoke();

            await UniTask.WhenAll(writeTask, waitTask);
            previewWindow.texture = webCamTexture;
        }

        public async UniTask<bool> WaitForReadyAsync(float timeout)
        {
            var startTime = Time.time;
            while (!IsReadyToCapture())
            {
                await UniTask.Delay(100);
                if (Time.time - startTime > timeout)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsReadyToCapture()
        {
            if (webCamTexture == null
                || !webCamTexture.isPlaying
                || !webCamTexture.isReadable
                || webCamTexture.width != webCamTexture.requestedWidth
                || webCamTexture.height != webCamTexture.requestedHeight
                || previewWindow.texture != webCamTexture)
            {
                return false;
            }
            return true;
        }

        public void SetCaption(string caption)
        {
            this.caption.text = caption;
        }

        // Self-timer
        public async UniTask<Texture2D> CaptureTextureWithTimerAsync(string caption, int timerSeconds, CancellationToken token)
        {
            if (!Launch(caption))
            {
                Debug.LogWarning("Camera is busy");
                return null;
            }

            try
            {
                if (!await WaitForReadyAsync(LaunchTimeout))
                {
                    Debug.LogError($"Failed to launch camera in {LaunchTimeout} seconds");
                    return null;
                }

                for (var i = timerSeconds; i > 0; i--)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    selfTimerCounter.text = i.ToString();
                    OnTimer?.Invoke(i);
                    await UniTask.Delay(1000, cancellationToken:token);
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }
                selfTimerCounter.text = string.Empty;

                var photo = CaptureAsTexture();

                if (PreviewTime > 0)
                {
                    // Preview captured photo when preview time is longer than zero
                    previewWindow.texture = photo;
                    await UniTask.Delay((int)(PreviewTime * 1000), cancellationToken: token);
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                return photo;
            }
            catch (Exception ex)
            {
                if (IsAlreadyLaunched)
                {
                    // Debug error when camera is launched
                    Debug.LogError($"Error occured in capturing photo with timer: {ex.Message}\n{ex.StackTrace}");
                }
            }
            finally
            {
                Close();
            }

            return null;
        }

        // QRCode/Barcode reader
        public async UniTask<string> ReadCodeAsync(CancellationToken token)
        {
            if (DecodeCode == null)
            {
                throw new Exception("ReadQRCode function is not implemented");
            }

            if (!Launch(CodeReaderCaption, CodeReaderFps))
            {
                Debug.LogWarning("Camera is busy");
                return string.Empty;
            }

            var result = string.Empty;

            try
            {
                if (!await WaitForReadyAsync(LaunchTimeout))
                {
                    Debug.LogError($"Failed to launch camera in {LaunchTimeout} seconds");
                    return string.Empty;
                }

                var startTime = Time.time;
                // Exit loop when camera is closed or cancel requested
                while (IsAlreadyLaunched && !token.IsCancellationRequested)
                {
                    var texture = GetTexture();
                    result = DecodeCode(texture);

                    if (!string.IsNullOrEmpty(result))
                    {
                        OnCodeRead?.Invoke(result);
                        break;
                    }
                    if (ReadTimeout > 0.0f && Time.time - startTime > ReadTimeout)
                    {
                        return string.Empty;    // Timeout
                    }

                    await UniTask.Delay(1000 / CodeReaderFps);
                }
            }
            catch (Exception ex)
            {
                if (IsAlreadyLaunched)
                {
                    // Debug error when camera is launched
                    Debug.LogError($"Error occured in reading QRCode: {ex.Message}\n{ex.StackTrace}");
                }
            }
            finally
            {
                Close();
            }

            return result;
        }
    }
}
