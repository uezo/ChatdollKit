using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;

namespace ChatdollKit.Examples.HelloWorld
{
    [RequireComponent(typeof(RequestProvider))]
    [RequireComponent(typeof(HelloDialog))]
    [RequireComponent(typeof(DialogRouter))]
    public class HelloWorld : MonoBehaviour
    {
        // Chatdoll component
        private Chatdoll chatdoll;

        // Message window
        public SimpleMessageWindow MessageWindow;

        private void Awake()
        {
            // Register handlers of Chatdoll
            chatdoll = gameObject.GetComponent<Chatdoll>();
            chatdoll.OnPromptAsync = OnPromptAsync;
            chatdoll.OnNoIntentAsync = OnNoIntentAsync;
            chatdoll.OnErrorAsync = OnErrorAsync;

            // Register resources of ModelController
            var modelController = gameObject.GetComponent<ModelController>();
            // Idle animations
            modelController.AddIdleAnimation("Default");
            // Voices
            foreach (var ac in Resources.LoadAll<AudioClip>("Voices"))
            {
                modelController.AddVoice(ac.name, ac);
            }

            // Register handlers of RequestProvider
            var rp = gameObject.GetComponent<RequestProvider>();
            rp.OnStartListeningAsync = OnStartListeningAsync;
            rp.OnFinishListeningAsync = OnFinishListeningAsync;
            rp.OnErrorAsync = OnErrorAsync;
        }

        // Chat
        public async Task ChatAsync()
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

        // Model actions for each status
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
            MessageWindow?.Show("(Listening...)");
        }

        public async Task OnFinishListeningAsync(Request request, Context context, CancellationToken token)
        {
            _ = MessageWindow?.SetMessageAsync(request.Text, token);
        }

        public async Task OnErrorAsync(Request request, Context context, CancellationToken token)
        {
        }
    }
}
