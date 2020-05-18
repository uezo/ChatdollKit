using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;


namespace ChatdollKit.Extension
{
    public class PhotoRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides photo request captured from camera
        public RequestType RequestType { get; } = RequestType.Camera;

        // Enabled
        public bool IsEnabled { get; set; } = false;

        // Camera
        private WebCamTexture webCamTexture;
        private bool listeningCamera = false;
        private byte[] photoBuffer;

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartCapturingAsync;
        public Func<Request, Context, CancellationToken, Task> OnFinishCapturingAsync;
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync;

        private void LateUpdate()
        {
            // Set pixel data to buffer from camera
            if (listeningCamera)
            {
                if (webCamTexture.didUpdateThisFrame)
                {
                    PictureToBuffer();
                }
            }
        }

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
            var request = new Request(RequestType, user);

            // Listen voice
            try
            {
                // Invoke action before start recognition
                await OnStartCapturingAsync?.Invoke(request, context, token);

                // Get picture from camera
                request.Payloads = await GetPictureAsync();
                if (request.IsSet())
                {
                    Debug.Log(request.Text);
                }
                else
                {
                    Debug.LogWarning("No photo captured");
                }
            }
            catch (TaskCanceledException)
            {
                Debug.Log("Canceled during capturing picture from camera");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in capturing picture from camera: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync?.Invoke(request, context, token);
            }
            finally
            {
                // Invoke action after recognition
                await OnFinishCapturingAsync?.Invoke(request, context, token);
            }

            return request;
        }

        // Get picture from camera
        public async Task<byte[]> GetPictureAsync(int fps = 15)
        {
            try
            {
                // Configure camera and launch
                webCamTexture = new WebCamTexture(null, 350, 350, fps);
                webCamTexture.Play();
                listeningCamera = true;
                // Wait for photo buffer updated
                photoBuffer = null;
                while (photoBuffer == null)
                {
                    await Task.Delay(1000 / fps);
                }
                return photoBuffer;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in taking photo: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                webCamTexture.Stop();
                listeningCamera = false;
            }
            return null;
        }

        // Set pixel data to buffer from camera
        private void PictureToBuffer()
        {
            if (webCamTexture == null)
            {
                return;
            }

            try
            {
                var photo = new Texture2D(webCamTexture.width, webCamTexture.height);
                photo.SetPixels32(webCamTexture.GetPixels32());
                photo.Apply();
                photoBuffer = photo.EncodeToJPG();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in getting pixels: {ex.Message}\n{ex.StackTrace}");
            }
        }

    }
}
