using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.Demo
{
    public class CommentContainer : MonoBehaviour
    {
        [SerializeField]
        private ScrollRect scrollRect;
        [SerializeField]
        private RectTransform contentPanel;

        public string CharacterName;
        public string DefaultUserName;

        [SerializeField]
        private GameObject characterCommentPrefab;
        [SerializeField]
        private GameObject userCommentPrefab;

        private bool shouldAddNewCommentItem = false;

        public void ResetCharacterComment()
        {
            shouldAddNewCommentItem = true;
        }

        public void AddCharacterComment(string comment)
        {
            var lastCommentTransform = contentPanel.Cast<Transform>().LastOrDefault();
            var commentItem = lastCommentTransform?.gameObject.GetComponent<CommentItem>();
            if (!shouldAddNewCommentItem && commentItem != null && commentItem.Name == CharacterName)
            {
                commentItem.Add(comment);
            }
            else
            {
                shouldAddNewCommentItem = false;
                AddComment(characterCommentPrefab, CharacterName, comment);
            }
        }

        public void AddUserComment(string comment, string userName = null)
        {
            AddComment(userCommentPrefab, string.IsNullOrEmpty(userName) ? DefaultUserName : userName, comment);
        }

        public void AddComment(GameObject itemPrefab, string name, string comment)
        {
            var newComment = Instantiate(itemPrefab, contentPanel);
            var commentItem = newComment.GetComponent<CommentItem>();
            commentItem.Name = name;
            commentItem.Body = comment;

            Canvas.ForceUpdateCanvases();
            scrollRect.normalizedPosition = new Vector2(0, 0);
        }
    }
}
