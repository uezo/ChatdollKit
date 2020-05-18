using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;


namespace ChatdollKit.Examples.ModelControl
{
    public class ModelControllerExample : MonoBehaviour
    {
        // ModelControllerコンポーネント
        private ModelController modelController;

        // CancellationToken
        private CancellationTokenSource cancelableTokenSource;


        private void Awake()
        {
            // ModelControllerの取得
            modelController = gameObject.GetComponent<ModelController>();

            // アイドル状態の定義
            modelController.AddIdleAnimation("Default");

            // 音声の登録
            foreach (var ac in Resources.LoadAll<AudioClip>("Voices"))
            {
                modelController.AddVoice(ac.name, ac);
            }

            // 笑顔の定義
            modelController.AddFace("Smile", new Dictionary<string, float>() {
                {"eyes_close_1", 100.0f }
            });
            // 悲しい顔の定義
            modelController.AddFace("Sad", new Dictionary<string, float>() {
                {"eyes_close_2", 15.0f },
                {"mouth_:0", 60.0f },
                {"mouth_:(", 70.0f },
            });
        }

        private void OnDestroy()
        {
            cancelableTokenSource?.Cancel();
        }

        public async Task Animate()
        {
            // アニメーション要求の作成
            var request = new AnimationRequest();

            // ベースレイヤーのアニメーション
            request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f);
            //request.AddAnimation("AGIA_Idle_brave_01_hand_on_chest", 3.0f);
            //request.AddAnimation("AGIA_Idle_energetic_01_right_fist_up", 3.0f);

            //// 上半身のアニメーション
            //request.AddAnimation("AGIA_Layer_swinging_body_01", "Upper Body", 2.0f);
            //request.AddAnimation("Default", "Upper Body", 2.0f);
            //request.AddAnimation("AGIA_Layer_look_away_01", "Upper Body", 2.0f);

            // アニメーションの実行
            await modelController.Animate(request, GetToken());
        }

        public async Task Say()
        {
            // 発声要求の作成
            var request = new VoiceRequest();
            request.AddVoice("line-girl1-yobimashita1");
            //request.AddVoice("line-girl1-yobimashita1", postGap: 1.0f);
            //request.AddVoice("line-girl1-haihaai1");
            //request.AddVoice("line-girl1-konnichiha1", preGap: 1.0f);

            // 発声の実行
            await modelController.Say(request, GetToken());
        }

        public async Task Face()
        {
            // まばたきの停止・表情設定・まばたき再開
            modelController.StopBlink();
            await modelController.SetFace("Smile", 2.0f);
            modelController.StartBlinkAsync();
        }

        public async Task AnimatedSay()
        {
            // 発話・アニメーション要求の作成
            var request = new AnimatedVoiceRequest();

            request.AddVoice("line-girl1-yobimashita1", 1.0f, 1.0f);
            request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");

            //request.AddVoice("line-girl1-yobimashita1");
            //request.AddAnimation("Default");

            //request.AddVoice("line-girl1-haihaai1", 1.0f, 1.0f, asNewFrame: true);
            //request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");
            //request.AddFace("Smile", 2.0f);   // 笑顔を2秒間継続
            //request.AddFace("Default");       // 元に戻す

            //request.AddVoice("line-girl1-konnichiha1", asNewFrame: true);
            //request.AddAnimation("Default");

            // 発話・アニメーションの実行
            await modelController.AnimatedSay(request, GetToken());
        }

        public void CancelRequest()
        {
            cancelableTokenSource?.Cancel();
            modelController.StartIdlingAsync();
        }

        private CancellationToken GetToken()
        {
            cancelableTokenSource?.Cancel();
            cancelableTokenSource = new CancellationTokenSource();
            return cancelableTokenSource.Token;
        }
    }
}
