using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;


namespace ChatdollKit.Dialog
{
    public class QRCodeRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides decoded QRCode data
        public RequestType RequestType { get; } = RequestType.QRCode;

        private ChatdollCamera chatdollCamera;

        private void Start()
        {
            chatdollCamera = GameObject.Find("ChatdollCamera").GetComponent<ChatdollCamera>();
        }

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token, Request preRequest = null)
        {
            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            var payloads = new List<string>();

            if (chatdollCamera != null)
            {
                payloads.Add(await chatdollCamera.ReadCodeAsync(token));
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
