using UnityEngine;

namespace ChatdollKit.UI
{
    public class UIControlContainer : MonoBehaviour
    {
        [SerializeField]
        private AIAvatar aiAvatar;
        [SerializeField]
        private ImageButton imageButton;
        [SerializeField]
        private MessageInput messageInput;

        private void Start()
        {
            if (aiAvatar == null)
            {
                aiAvatar = FindFirstObjectByType<AIAvatar>();
                if (aiAvatar == null)
                {
                    Debug.LogWarning("AIAvatar is not found in this scene.");
                }
            }

            if (imageButton != null && imageButton.HandleImage == null)
            {
                imageButton.HandleImage = (imageBytes) =>
                {
                    messageInput.SetImageToPreview(imageBytes);
                };
            }
        }
    }
}
