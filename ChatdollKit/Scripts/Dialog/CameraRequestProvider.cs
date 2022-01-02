using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;

namespace ChatdollKit.Dialog
{
    public class CameraRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides photo taken by camera
        public RequestType RequestType { get; } = RequestType.Camera;

        public string CameraCaption;
        public int SelfTimerSeconds = 3;

        public ChatdollCamera ChatdollCamera;

        // Create request using voice recognition
        public async UniTask<Request> GetRequestAsync(User user, State state, CancellationToken token, Request preRequest = null)
        {
            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            if (ChatdollCamera != null)
            {
                request.Payloads.Add(await ChatdollCamera.CaptureTextureWithTimerAsync(CameraCaption, SelfTimerSeconds, token));
            }
            else
            {
                Debug.LogError("ChatdollCamera is not set to CameraRequestProvider");
            }

            return request;
        }
    }
}
