using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.UI
{
    public class MessageInput : MonoBehaviour
    {
        [SerializeField]
        private DialogProcessor dialogProcessor;
        [SerializeField]
        private SimpleCamera simpleCamera;

        // Input UI
        [SerializeField]
        private InputField messageInput;
        [SerializeField]
        private RectTransform messageTextRect;
        [SerializeField]
        private RectTransform messagePlaceholderRect;

        [SerializeField]
        private Image imagePreview;

        private void Start()
        {
            if (dialogProcessor == null)
            {
                dialogProcessor = FindFirstObjectByType<DialogProcessor>();
                if (dialogProcessor == null)
                {
                    Debug.LogWarning("DialogProcessor is not found in this scene.");
                }
            }

            if (simpleCamera == null)
            {
                simpleCamera = FindFirstObjectByType<SimpleCamera>();
                if (simpleCamera == null)
                {
                    Debug.LogWarning("SimpleCamera is not found in this scene.");
                }
            }
        }

        // Conversation UI
        public void OnSubmitMessageInput()
        {
            // Text
            var inputText = messageInput.text.Trim();
            messageInput.text = string.Empty;
            if (string.IsNullOrEmpty(inputText)) return;

            // Image
            var payloads = new Dictionary<string, object>();
            var imageBytes = GetImageFromPreview();
            if (imageBytes != null)
            {
                payloads["imageBytes"] = imageBytes;
                ClearImage();
            }
            else if (simpleCamera != null && simpleCamera.IsAlreadyStarted)
            {
                imageBytes = simpleCamera.GetStillImage();
                if (imageBytes != null)
                {
                    payloads["imageBytes"] = imageBytes;
                    simpleCamera.ClearStillImage();
                }
            }

            // Chat
            _ = dialogProcessor.StartDialogAsync(inputText, payloads);
        }

        public byte[] GetImageFromPreview()
        {
            if (imagePreview.sprite != null)
            {
                return imagePreview.sprite.texture.EncodeToJPG();
            }
            else
            {
                return null;
            }
        }

        public void SetImageToPreview(byte[] imageBytes)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(imageBytes);

            // Resize image
            var resizedTexture = ResizeTexture(texture, 640);

            // Set image to preview
            var sprite = Sprite.Create(resizedTexture, new Rect(0.0f, 0.0f, resizedTexture.width, resizedTexture.height), new Vector2(0.5f, 0.5f));
            imagePreview.preserveAspect = true;
            imagePreview.sprite = sprite;
            imagePreview.gameObject.SetActive(true);

            // Adjust text input
            messageTextRect.offsetMin = new Vector2(75, messageTextRect.offsetMin.y);
            messagePlaceholderRect.offsetMin = new Vector2(75, messagePlaceholderRect.offsetMin.y);
        }

        public void ClearImage()
        {
            imagePreview.sprite = null;
            imagePreview.gameObject.SetActive(false);
            
            // Adjust text input
            messageTextRect.offsetMin = new Vector2(10, messageTextRect.offsetMin.y);
            messagePlaceholderRect.offsetMin = new Vector2(10, messagePlaceholderRect.offsetMin.y);
        }

        private static Texture2D ResizeTexture(Texture2D originalTexture, int maxLength)
        {
            var width = originalTexture.width;
            var height = originalTexture.height;

            if (Mathf.Max(width, height) < maxLength)
            {
                // Use original texture if smaller than the max
                return originalTexture;
            }

            // Calculate the resized size keeping aspect ratio
            var aspect = (float)width / height;
            if (width > height)
            {
                width = maxLength;
                height = Mathf.RoundToInt(maxLength / aspect);
            }
            else
            {
                height = maxLength;
                width = Mathf.RoundToInt(maxLength * aspect);
            }

            // Make resized texture
            var resizedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / (width - 1);
                    float v = (float)y / (height - 1);
                    resizedTexture.SetPixel(x, y, originalTexture.GetPixelBilinear(u, v));
                }
            }
            resizedTexture.Apply();

            return resizedTexture;
        }
    }
}
