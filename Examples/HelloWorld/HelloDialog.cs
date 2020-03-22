using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;


namespace ChatdollKit.Examples
{
    public class HelloDialog : MonoBehaviour, IDialogProcessor
    {
        public string TopicName { get; } = "hello";

        private ModelController modelController;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
        }

        public void Configure()
        {
            // 何もしない
        }

        public async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            // 何らかの処理

            // インテント抽出結果の表示アクションを定義
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddVoice("line-girl1-konnichiha1", preGap: 1.0f, postGap: 2.0f);
            animatedVoiceRequest.AddAnimation("Default");

            // レスポンス
            var response = new Response(request.Id);
            response.Payloads = animatedVoiceRequest;
            return response;
        }

        public async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {
            var animatedVoiceRequest = response.Payloads as AnimatedVoiceRequest;
            await modelController.AnimatedSay(animatedVoiceRequest, token);
        }
    }
}
