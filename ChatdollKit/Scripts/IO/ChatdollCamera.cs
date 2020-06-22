using System;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.IO;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif


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
        public int PreviewTime = 2000;
        public AudioClip CaptureSound;

        // Preview show/hide control
        public Action<GameObject, GameObject> ShowPreview;
        public Action<GameObject, GameObject> HidePreview;
        // Action called when captured photo
        public Action OnCaptured;

        // Components
        private WebCamTexture webCamTexture;
        private RawImage previewWindow;
        private GameObject backgroundPanel;
        private Text caption;
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
        }

        private void Start()
        {
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

            caption = gameObject.GetComponentInChildren<Text>(true);
            if (caption == null)
            {
                Debug.LogWarning("Caption is not found");
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

        public void Launch(string caption = null)
        {
            try
            {
                // Configure camera and launch
                webCamTexture = new WebCamTexture(Size.x, Size.y, Fps);
                previewWindow.texture = webCamTexture;
                webCamTexture.Play();

                // Set caption
                this.caption.text = caption ?? string.Empty;

                // Show preview
                (ShowPreview ?? ShowPreviewDefault).Invoke(previewWindow.gameObject, backgroundPanel);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in starting camera: {ex.Message}\n{ex.StackTrace}");
                webCamTexture.Stop();
            }
        }

        public void Close()
        {
            (HidePreview ?? HidePreviewDefault).Invoke(previewWindow.gameObject, backgroundPanel);
            webCamTexture?.Stop();
            webCamTexture = null;
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
            if (!IsReadyToCapture())
            {
                throw new Exception("Camera is not ready to capture");
            }

            // Get texture from WebCamTexture
            var photo = GetTexture();

            audioSource.Play();
            OnCaptured?.Invoke();

            // Return byte array of JPEG
            return photo.EncodeToJPG();
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

        public async Task CaptureAsync(string path)
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
            var waitTask = Task.Delay(PreviewTime);

            // Save as file
            var img = photo.EncodeToJPG();
            var st = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
            var writeTask = st.WriteAsync(img, 0, img.Length);

            audioSource.Play();
            OnCaptured?.Invoke();

            await Task.WhenAll(writeTask, waitTask);
            previewWindow.texture = webCamTexture;
        }
    }
}
