using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using ChatdollKit.Extension;
using ChatdollKit.Dialog;
using System.Threading;
using ChatdollKit.Model;


namespace ChatdollKit.Examples
{
    [RequireComponent(typeof(DummyRequestProvider))]
    [RequireComponent(typeof(IntentExtractor))]
    [RequireComponent(typeof(HelloDialog))]
    public class HelloWorldExample : MonoBehaviour
    {
        // Chatdollコンポーネント
        private Chatdoll chatdoll;

        // メッセージウィンドウ
        public SimpleMessageWindow MessageWindow;

        private void Awake()
        {
            // ChatdollKitの取得
            chatdoll = gameObject.GetComponent<Chatdoll>();

            // アイドル状態の定義
            chatdoll.ModelController.AddIdleAnimation("Default");

            // 音声
            foreach (var ac in Resources.LoadAll<AudioClip>("Voices"))
            {
                chatdoll.ModelController.AddVoice(ac.name, ac);
            }

            // ステータス毎のアクションの登録
            chatdoll.OnPromptAsync = OnPromptAsync;
            chatdoll.OnNoIntentAsync = OnNoIntentAsync;
            chatdoll.OnErrorAsync = OnErrorAsync;

            // リクエスト取得に関わるスタータス毎のアクションの登録
            var rp = gameObject.GetComponent<DummyRequestProvider>();
            rp.OnStartListeningAsync = OnStartListeningAsync;
            rp.OnFinishListeningAsync = OnFinishListeningAsync;
            rp.OnErrorAsync = OnErrorAsync;
        }

        // 対話
        private async Task ChatAsync()
        {
            try
            {
                await chatdoll.StartChatAsync("User1234567890");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in chat: {ex.Message}\n{ex.StackTrace}");
            }

        }

        // インスペクター上のチャット開始ボタン
        [CustomEditor(typeof(HelloWorldExample))]
        public class AppEditorInterface : Editor
        {
            public override void OnInspectorGUI()
            {
                base.OnInspectorGUI();

                var app = target as HelloWorldExample;

                if (GUILayout.Button("Start Chat"))
                {
                    _ = app.ChatAsync();
                }
            }
        }

        // ステータス毎のアクション
        public async Task OnPromptAsync(User user, Context context, CancellationToken token)
        {
            var request = new AnimatedVoiceRequest(startIdlingOnEnd: false);
            request.AddAnimatedVoice("line-girl1-yobimashita1", "AGIA_Idle_angry_01_hands_on_waist", voicePreGap: 0.5f);
            await chatdoll.ModelController.AnimatedSay(request, token);
        }

        public async Task OnNoIntentAsync(Request request, Context context, CancellationToken token)
        {
        }

        public async Task OnStartListeningAsync(Request request, Context context, CancellationToken token)
        {
            MessageWindow.Show("(Listening...)");
        }

        public async Task OnFinishListeningAsync(Request request, Context context, CancellationToken token)
        {
            _ = MessageWindow.SetMessageAsync(request.Text, token);
        }

        public async Task OnErrorAsync(Request request, Context context, CancellationToken token)
        {
        }
    }
}
