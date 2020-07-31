using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;


namespace ChatdollKit.Dialog
{
    public class CameraRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides photo taken by camera
        public RequestType RequestType { get; } = RequestType.Camera;

        public string CameraCaption;
        public int SelfTimerSeconds = 3;

        private ChatdollCamera chatdollCamera;

        private void Start()
        {
            chatdollCamera = GameObject.Find("ChatdollCamera").GetComponent<ChatdollCamera>();
        }

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
            var request = new Request(RequestType, user);
            var payloads = new List<Texture2D>();

            if (chatdollCamera != null)
            {
                payloads.Add(await chatdollCamera.CaptureTextureWithTimerAsync(CameraCaption, SelfTimerSeconds, token));
            }
            else
            {
                Debug.LogWarning("ChatdollCamera is not found");
            }

            request.Payloads = payloads;
            return request;
        }
    }
}
