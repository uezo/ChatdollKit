using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.Demo
{
    public class CommentItem : MonoBehaviour
    {
        [SerializeField]
        private Text nameText;
        [SerializeField]
        private Text bodyText;

        public string Name
        {
            get { return nameText.text; }
            set { nameText.text = value; }
        }

        public string Body
        {
            get { return bodyText.text; }
            set { bodyText.text = value; }
        }

        public void Add(string text)
        {
            bodyText.text += text;
        }
    }
}
