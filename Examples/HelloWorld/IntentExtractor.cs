using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;


namespace ChatdollKit.Examples
{
    public class IntentExtractor : MonoBehaviour, IIntentExtractor
    {
        private ModelController modelController;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
        }

        public void Configure()
        {
            // 何もしない
        }

        // インテントとエンティティの抽出
        public async Task<Response> ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            // インテントを抽出（常に hello）してリクエストを更新
            request.Intent = "hello";

            // インテント抽出結果の表示アクションを定義
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddVoice("line-girl1-haihaai1", preGap: 1.0f, postGap: 2.0f);
            animatedVoiceRequest.AddAnimation("Default");

            // レスポンス
            var response = new Response(request.Id);
            response.Payloads = animatedVoiceRequest;
            return response;
        }

        // インテント抽出結果の表示
        public async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {
            var animatedVoiceRequest = response.Payloads as AnimatedVoiceRequest;
            await modelController.AnimatedSay(animatedVoiceRequest, token);
        }
    }
}
