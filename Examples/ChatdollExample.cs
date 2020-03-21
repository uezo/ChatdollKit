using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using com.gateboxlab.gateboxsdk;
using ChatdollKit.Extension;


namespace ChatdollKit.Examples
{
    public class ChatdollExample : MonoBehaviour
    {
        // Chatdollコンポーネント
        private Chatdoll chatdoll;

        // Gateboxボタン押下イベント2回発生対応
        private float lastGBPushed = 0.0f;

        private void Awake()
        {
            // ChatdollKitの取得
            chatdoll = gameObject.GetComponent<Chatdoll>();

            // アイドル状態の定義（Anime Girl Idle Animationsを使用した場合の例）
            // https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-150397
            chatdoll.ModelController.AddIdleAnimation("Default");
            chatdoll.ModelController.AddIdleAnimation("AGIA_Idle_classy_01_left_hand_on_waist");
            chatdoll.ModelController.AddIdleAnimation("Default");
            chatdoll.ModelController.AddIdleAnimation("AGIA_Layer_swing_body_01", "Upper Body", addToLastRequest: true);

            // 表情
            chatdoll.ModelController.AddFace("Smile", new Dictionary<string, float>() {
                {"eyes_close_1", 1.0f }
            });

            // 音声
            foreach (var ac in Resources.LoadAll<AudioClip>("Voices"))
            {
                chatdoll.ModelController.AddVoice(ac.name, ac);
            }

            // ステータス毎のアクションの登録
            var ma = gameObject.GetComponent<ModelActions>();
            chatdoll.OnPromptAsync = ma.OnPromptAsync;
            chatdoll.OnNoIntentAsync = ma.OnNoIntentAsync;
            chatdoll.OnErrorAsync = ma.OnErrorAsync;

            // リクエスト取得に関わるスタータス毎のアクションの登録
            //var rp = gameObject.GetComponent<AzureVoiceRequestProvider>();    // Azureのときはこちら。Macでは使用不可
            var rp = gameObject.GetComponent<GoogleCloudSpeechRequestProvider>();   // Googleのときはこちら。要有償アセット
            rp.OnStartListeningAsync = ma.OnStartListeningAsync;
            rp.OnFinishListeningAsync = ma.OnFinishListeningAsync;
            rp.OnErrorAsync = ma.OnErrorAsync;
        }

        private void Start()
        {
            // ステージLEDをデフォルト色に変更
            StageLED.SetColor(Color.cyan);

            // Gateboxボタンの押下を監視
            GateboxButton.RegisterListener(gameObject.name, "OnGateboxButtonPushed");
        }

        // Gateboxボタン押下時のイベントハンドラ
        private async Task OnGateboxButtonPushed()
        {
            // 2秒以内のダブルタップの場合は無視（ちょっと適当。なぜか実機で2回イベントが発生するため）
            var now = Time.time;
            if (now - lastGBPushed < 2.0f)
            {
                Debug.Log("ダブルタップのため無視します: " + (now - lastGBPushed).ToString());
                return;
            }
            lastGBPushed = now;

            // 対話
            try
            {
                var customerId = GateboxDevices.GetCustomerID() ?? "User1234567890";
                await chatdoll.StartChatAsync(customerId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in chat: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // インスペクター上のGateboxボタン代替
        [CustomEditor(typeof(ChatdollExample))]
        public class AppEditorInterface : Editor
        {
            public override void OnInspectorGUI()
            {
                base.OnInspectorGUI();

                var app = target as ChatdollExample;

                if (GUILayout.Button("Gatebox Button"))
                {
                    _ = app.OnGateboxButtonPushed();
                }
            }
        }
    }
}
