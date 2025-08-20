using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.UI
{
    public class MessageWindowContainer : MonoBehaviour
    {
        [SerializeField]
        private AIAvatar aiAvatar;
        [SerializeField]
        private MessageWindowBase userMessageWindow;
        [SerializeField]
        private MessageWindowBase characterMessageWindow;
        [SerializeField]
        private bool StopChatOnUserMessageWindowClick;

        private void Start()
        {
            if (aiAvatar == null)
            {
                aiAvatar = FindFirstObjectByType<AIAvatar>();
                if (aiAvatar == null)
                {
                    Debug.LogWarning("AIAvatar is not found in this scene.");
                }
                else
                {
                    aiAvatar.UserMessageWindow = userMessageWindow;
                    aiAvatar.CharacterMessageWindow = characterMessageWindow;
                }
            }
        }

        public void OnCharacterMessageWindowClick()
        {
            if (StopChatOnUserMessageWindowClick && aiAvatar != null && characterMessageWindow != null)
            {
                aiAvatar.StopChatAsync(continueDialog: true).Forget();
            }
        }
    }
}
