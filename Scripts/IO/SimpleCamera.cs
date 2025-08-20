using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
using Cysharp.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using Newtonsoft.Json;
using System.Runtime.InteropServices;
#endif

namespace ChatdollKit.IO
{
    public class SimpleCamera : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GetCameraDevices(string objectName, string functionName);
#endif

        [SerializeField]
        private RawImage previewWindow;
        public string DeviceName;
        public string SubDeviceName;
        [SerializeField]
        private Vector2Int size = new Vector2Int(640, 480);
        [SerializeField]
        private int fps = 10;
        [SerializeField]
        private float launchTimeout = 10.0f;
        [SerializeField]
        private float waitAfterStart = 0.5f;
        [SerializeField]
        private bool PrintDevicesOnStart;
        [SerializeField]
        private string savePathForDebug;
        [SerializeField]
        private Image stillImage;
        [SerializeField]
        private Image stillFadeImage;
        [SerializeField]
        private Button stillImageButton;
        [SerializeField]
        private Button toggleDeviceButton;

        private string currentDeviceName { get; set; }
        public bool IsCameraEnabled { get; private set; } = false;
        public bool IsAlreadyStarted { get; private set; } = false;
        private WebCamTexture webCamTexture;
        public List<CameraDeviceInfo> CameraDevices { get; private set; } = new List<CameraDeviceInfo>();
        [SerializeField]
        private List<string> unavailableCameraKeywords;

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
            Stop();
        }

        public async UniTask StartAsync(bool showPreview = true)
        {
            if (IsAlreadyStarted) return;

            try
            {
                if (CameraDevices.Count == 0)
                {
                    await LoadDevices();
                }

                // Configure and start camera
                Debug.Log($"Start camera: {currentDeviceName}");
                webCamTexture = new WebCamTexture(currentDeviceName, size.x, size.y, fps > 0 ? fps : 10);
                previewWindow.texture = webCamTexture;
                webCamTexture.Play();

                // Wait
                if (!await WaitForReadyAsync(launchTimeout))
                {
                    Debug.LogError($"Failed to launch camera '{currentDeviceName}' in {launchTimeout} seconds");
                    return;
                }

                // Preview
                if (showPreview)
                {
                    AdjustAspectRatio();

                    foreach (var device in CameraDevices)
                    {
                        if (device.Name == webCamTexture.deviceName)
                        {
#if PLATFORM_IOS && !UNITY_EDITOR
                            if (device.IsFrontFacing)
                            {
                                previewWindow.transform.rotation = Quaternion.Euler(0, 0, -webCamTexture.videoRotationAngle);
                            }
                            else
                            {
                                previewWindow.transform.rotation = Quaternion.Euler(0, 180, -webCamTexture.videoRotationAngle);
                            }
#else
                            if (device.IsFrontFacing)
                            {
                                previewWindow.transform.rotation = Quaternion.Euler(0, 180, -webCamTexture.videoRotationAngle);
                            }
                            else
                            {
                                previewWindow.transform.rotation = Quaternion.Euler(0, 0, -webCamTexture.videoRotationAngle);
                            }
# endif
                            break;
                        }
                    }

                    AdjustStillImageComponentsSize();
                    previewWindow.gameObject.SetActive(true);

                    if (!string.IsNullOrEmpty(SubDeviceName))
                    {
                        toggleDeviceButton.gameObject.SetActive(true);
                    }
                }

                IsAlreadyStarted = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in starting camera: {ex.Message}\n{ex.StackTrace}");
                webCamTexture?.Stop();
            }
        }

        private void AdjustStillImageComponentsSize()
        {
            // Calculate preview size
            var previewWindowTransform = previewWindow.GetComponent<RectTransform>();
            float previewWidth = previewWindowTransform.rect.width;
            float previewHeight = previewWindowTransform.rect.height;
            float textureAspectRatio = (float)webCamTexture.width / webCamTexture.height;
            float adjustedWidth, adjustedHeight;
            if (previewWidth / previewHeight > textureAspectRatio)
            {
                adjustedHeight = previewHeight;
                adjustedWidth = adjustedHeight * textureAspectRatio;
            }
            else
            {
                adjustedWidth = previewWidth;
                adjustedHeight = adjustedWidth / textureAspectRatio;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            Vector2 previewSize;
            if (Application.isMobilePlatform)
            {
                previewSize = new Vector2(adjustedHeight, adjustedWidth);
            }
            else
            {
                previewSize = new Vector2(adjustedWidth, adjustedHeight);
            }
#else
            var previewSize = new Vector2(adjustedWidth, adjustedHeight);
#endif

            // Still button
            var buttonRectTransform = stillImageButton.GetComponent<RectTransform>();
            buttonRectTransform.sizeDelta = previewSize;

            // Still image
            var fadeImageRectTransform = stillFadeImage.GetComponent<RectTransform>();
            fadeImageRectTransform.sizeDelta = previewSize;
        }

        public void Stop()
        {
            previewWindow.gameObject.SetActive(false);
            toggleDeviceButton.gameObject.SetActive(false);
            webCamTexture?.Stop();
            ClearStillImage();
            IsAlreadyStarted = false;
        }

        public void ToggleCamera()
        {
            if (IsAlreadyStarted)
            {
                Stop();
            }
            else
            {
                _ = StartAsync();
            }
        }

        public void ToggleDevice()
        {
            if (!IsAlreadyStarted) return;
            if (string.IsNullOrEmpty(SubDeviceName)) return;

            if (currentDeviceName == DeviceName)
            {
                currentDeviceName = SubDeviceName;
            }
            else
            {
                currentDeviceName = DeviceName;
            }

            Stop();
            _ = StartAsync();
        }

        public async UniTask<byte[]> CaptureImageAsync()
        {
            // Start camera if not started
            var cameraIsStarted = IsAlreadyStarted;
            if (!cameraIsStarted)
            {
                await StartAsync(showPreview: false);
                await UniTask.Delay((int)(waitAfterStart * 1000));   // Wait a bit to ensure capturing
            }

            // Take photo
            var photo = new Texture2D(webCamTexture.width, webCamTexture.height);
            photo.SetPixels32(webCamTexture.GetPixels32());
            photo.Apply();

            photo = RotateTexture(photo, webCamTexture.videoRotationAngle);

            // Stop camera if started at this method
            if (!cameraIsStarted)
            {
                Stop();
            }

            // Encode to JPG
            var jpg = photo.EncodeToJPG();

            // Save for debug
            if (!string.IsNullOrEmpty(savePathForDebug))
            {
                var st = new FileStream(savePathForDebug, FileMode.OpenOrCreate, FileAccess.Write);
                await st.WriteAsync(jpg, 0, jpg.Length).AsUniTask();
            }

            return jpg;
        }

        public async UniTask LoadDevices()
        {
            // Load devices
#if UNITY_WEBGL && !UNITY_EDITOR
            GetCameraDevices(gameObject.name, "HandleCameraDeviceNames");
            var startTime = Time.time;
            while (CameraDevices.Count == 0)
            {
                if ((Time.time - startTime) >= 5f)
                {
                    Debug.LogWarning("No camera devices found.");
                    break;
                }
                await UniTask.Delay(16);    // 60FPS
            }
#else
            CameraDevices.Clear();
            foreach (var d in WebCamTexture.devices)
            {
                CameraDevices.Add(CameraDeviceInfo.FromWebCamDevice(d));
            }
#endif

            // Remove unavailable devices
            CameraDevices.RemoveAll(d =>
                unavailableCameraKeywords.Any(keyword =>
                    d.Name.ToLower().Contains(keyword.ToLower())));

            // Select main and sub device
            foreach (var d in CameraDevices)
            {
                if (d.IsFrontFacing)
                {
                    SubDeviceName = d.Name;
                }
                else
                {
                    DeviceName = d.Name;
                }
                if (!string.IsNullOrEmpty(DeviceName) && !string.IsNullOrEmpty(SubDeviceName))
                {
                    break;
                }
            }

            // Fallback
            DeviceName = string.IsNullOrEmpty(DeviceName) && CameraDevices.Count > 0
                ? CameraDevices[0].Name : DeviceName;
            DeviceName = string.IsNullOrEmpty(SubDeviceName) && CameraDevices.Count > 1
                ? CameraDevices[1].Name : DeviceName;

            currentDeviceName = DeviceName;

            Debug.Log($"Set camera devices: main={DeviceName} / sub={SubDeviceName}");

            if (PrintDevicesOnStart)
            {
                Debug.Log($"==== Camera Device List ({CameraDevices.Count}) ====");
                foreach (var d in CameraDevices)
                {
                    Debug.Log($"{d.Name} ({d.IsFrontFacing})");
                }
            }
        }

        private async UniTask<bool> WaitForReadyAsync(float timeout)
        {
            var startTime = Time.time;
            while (webCamTexture == null
                || !webCamTexture.isPlaying
                || !webCamTexture.isReadable
                || webCamTexture.width != webCamTexture.requestedWidth
                || webCamTexture.height != webCamTexture.requestedHeight
                || previewWindow.texture != webCamTexture
            )
            {
                await UniTask.Delay(100);
                if (Time.time - startTime > timeout)
                {
                    return false;
                }
            }

            return true;
        }

        private void AdjustAspectRatio()
        {
            var imageAspectRatio = (float)webCamTexture.width / webCamTexture.height;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Application.isMobilePlatform)
            {
                imageAspectRatio = 1 / imageAspectRatio;
            }
#endif
            var windowAspectRatio = previewWindow.rectTransform.rect.width / previewWindow.rectTransform.rect.height;

            Debug.Log($"Aspect Ratio: Image={imageAspectRatio} / Preview={windowAspectRatio}");

            if (imageAspectRatio > windowAspectRatio)
            {
                var scaleHeight = windowAspectRatio / imageAspectRatio;
                Debug.Log($"Adjust to landscape 1:{scaleHeight}");
                previewWindow.rectTransform.localScale = new Vector3(1, scaleHeight, 1);
            }
            else
            {
                var scaleWidth = imageAspectRatio / windowAspectRatio;
                Debug.Log($"Adjust to portrait {scaleWidth}:1");
                previewWindow.rectTransform.localScale = new Vector3(scaleWidth, 1, 1);
            }
        }

        private Texture2D RotateTexture(Texture2D originalTexture, int angle)
        {
            int width = originalTexture.width;
            int height = originalTexture.height;
            Texture2D rotatedTexture;

            angle = (angle % 360 + 360) % 360;

            if (angle == 270)
            {
                rotatedTexture = new Texture2D(height, width);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(height - y - 1, x, originalTexture.GetPixel(x, y));
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(y, width - x - 1, originalTexture.GetPixel(x, y));
                    }
                }

            }
            else if (angle == 180)
            {
                rotatedTexture = new Texture2D(width, height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(width - x - 1, height - y - 1, originalTexture.GetPixel(x, y));
                    }
                }
            }
            else if (angle == 90)
            {
                rotatedTexture = new Texture2D(height, width);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(y, width - x - 1, originalTexture.GetPixel(x, y));
                    }
                }
            }
            else
            {
                rotatedTexture = new Texture2D(width, height);
                Graphics.CopyTexture(originalTexture, rotatedTexture);
            }

            rotatedTexture.Apply();
            return rotatedTexture;
        }

        public void OnStillImageButton()
        {
            if (stillImage.sprite == null && IsAlreadyStarted)
            {
                LoadStillImageAsync();
            }
            else
            {
                ClearStillImage();
            }
        }

        public async void LoadStillImageAsync()
        {
            var imageBytes = await CaptureImageAsync();

            var texture = new Texture2D(2, 2);
            texture.LoadImage(imageBytes);

#if UNITY_WEBGL && !UNITY_EDITOR
            if (Application.isMobilePlatform)
            {
                texture = FixWebGLMobileAspect(texture);
            }
#endif

            // Set image to preview
            var sprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            stillImage.preserveAspect = true;
            stillImage.sprite = sprite;

            // Fade effect
            StartCoroutine(StillButtonFadeEffect());

            stillImage.gameObject.SetActive(true);
        }

        private Texture2D FixWebGLMobileAspect(Texture2D texture)
        {
            if (texture.height > texture.width)
            {
                return texture;
            }
            
            var targetWidth = texture.height;
            var targetHeight = texture.width;
            
            var fixedTexture = new Texture2D(targetWidth, targetHeight);
            
            for (var y = 0; y < targetHeight; y++)
            {
                for (var x = 0; x < targetWidth; x++)
                {
                    var u = (float)x / targetWidth;
                    var v = (float)y / targetHeight;
                    fixedTexture.SetPixel(x, y, texture.GetPixelBilinear(u, v));
                }
            }
            
            fixedTexture.Apply();
            Destroy(texture);
            
            return fixedTexture;
        }

        public void ClearStillImage()
        {
            stillImage.sprite = null;
            stillImage.gameObject.SetActive(false);
        }

        public byte[] GetStillImage(bool clear = false)
        {
            if (stillImage.sprite != null)
            {
                var imageBytes = stillImage.sprite.texture.EncodeToJPG();
                if (clear)
                {
                    ClearStillImage();
                }
                return imageBytes;
            }
            return null;
        }

        public void SetStillImage(byte[] imageBytes)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(imageBytes);

            // Set image to preview
            var sprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            stillImage.preserveAspect = true;
            stillImage.sprite = sprite;

            stillImage.gameObject.SetActive(true);            
        }

        private IEnumerator StillButtonFadeEffect()
        {
            float originalAlpha = stillFadeImage.color.a;

            stillFadeImage.color = new Color(stillFadeImage.color.r, stillFadeImage.color.g, stillFadeImage.color.b, 0.7f);
            stillFadeImage.gameObject.SetActive(true);

            yield return new WaitForSeconds(0.1f);

            float elapsedTime = 0f;
            while (elapsedTime < 0.1f)
            {
                elapsedTime += Time.deltaTime;
                float alpha = Mathf.Lerp(0.7f, originalAlpha, elapsedTime / 0.1f);
                stillFadeImage.color = new Color(stillFadeImage.color.r, stillFadeImage.color.g, stillFadeImage.color.b, alpha);
                yield return null;
            }

            stillFadeImage.color = new Color(stillFadeImage.color.r, stillFadeImage.color.g, stillFadeImage.color.b, originalAlpha);
            stillFadeImage.gameObject.SetActive(false);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public void HandleCameraDeviceNames(string deviceNamesJsonString)
        {
            Debug.Log($"Camera devices: {deviceNamesJsonString}");
            CameraDevices = CameraDeviceInfo.FromJsonString(deviceNamesJsonString);
        }
#endif
    }

    public enum CameraDeviceType
    {
        UNKNOWN, OUT, IN
    }

    public class CameraDeviceInfo
    {
        public string Name { get; set; }
        public CameraDeviceType CameraDeviceType { get; set; }
        public bool IsFrontFacing { get { return this.CameraDeviceType == CameraDeviceType.IN; } }
        public int MaxResolutionX { get; set; }
        public int MaxResolutionY { get; set; }

        private static CameraDeviceType DetectType(bool isFrontFacing, string name)
        {
            if (isFrontFacing)
            {
                return CameraDeviceType.IN;
            }

            var lowerName = name.ToLower();

            if (lowerName.Contains("front") ||
                lowerName.Contains("前") ||
                lowerName.Contains("user") ||
                lowerName.Contains("facetime"))
            {
                return CameraDeviceType.IN;
            }
            else if (lowerName.Contains("back") ||
                lowerName.Contains("rear") ||
                lowerName.Contains("後") ||
                lowerName.Contains("environment"))
            {
                return CameraDeviceType.OUT;
            }

            return CameraDeviceType.UNKNOWN;
        }

        public static CameraDeviceInfo FromWebCamDevice(WebCamDevice device)
        {
            return new CameraDeviceInfo()
            {
                Name = device.name,
                CameraDeviceType = DetectType(device.isFrontFacing, device.name)
            };
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public static List<CameraDeviceInfo> FromJsonString(string devicesJsonString)
        {
            var devices = new List<CameraDeviceInfo>();
            foreach (var name in JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(devicesJsonString)["names"])
            {
                devices.Add(new CameraDeviceInfo()
                {
                    Name = name,
                    CameraDeviceType = DetectType(false, name),
                });
            }
            return devices;
        }
#endif
    }
}
