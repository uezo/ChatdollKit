using System;
using UnityEngine;

namespace ChatdollKit.UI.ConversationControls
{
    /// <summary>Owns a decoded attachment and the JPEG sent to either pipeline.</summary>
    internal sealed class ConversationImage : IDisposable
    {
        internal readonly byte[] JpegBytes;
        internal readonly Sprite Sprite;
        private readonly Texture2D texture;

        private ConversationImage(Texture2D texture)
        {
            this.texture = texture;
            JpegBytes = texture.EncodeToJPG();
            Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        internal static ConversationImage Create(byte[] bytes, int maxDimension)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("The image is empty.", nameof(bytes));
            Texture2D original = null, resized = null;
            try
            {
                original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!original.LoadImage(bytes)) throw new ArgumentException("The selected file is not a supported image.", nameof(bytes));
                maxDimension = Math.Max(1, maxDimension);
                if (Math.Max(original.width, original.height) > maxDimension)
                {
                    var scale = (float)maxDimension / Math.Max(original.width, original.height);
                    var width = Math.Max(1, Mathf.RoundToInt(original.width * scale));
                    var height = Math.Max(1, Mathf.RoundToInt(original.height * scale));
                    resized = new Texture2D(width, height, TextureFormat.RGB24, false);
                    var pixels = new Color[width * height];
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                            pixels[y * width + x] = original.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height);
                    resized.SetPixels(pixels);
                    resized.Apply();
                }
                var result = new ConversationImage(resized != null ? resized : original);
                if (resized != null) UnityEngine.Object.Destroy(original);
                original = resized = null;
                return result;
            }
            finally
            {
                if (original != null) UnityEngine.Object.Destroy(original);
                if (resized != null) UnityEngine.Object.Destroy(resized);
            }
        }

        public void Dispose()
        {
            if (Sprite != null) UnityEngine.Object.Destroy(Sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}
