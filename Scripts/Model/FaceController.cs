using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChatdollKit.Model
{
    public class FaceController : MonoBehaviour
    {
        [Header("Face")]
        public SkinnedMeshRenderer SkinnedMeshRenderer;
        public float DefaultFaceExpressionDuration = 7.0f;
        private IFaceExpressionProxy faceExpressionProxy;
        private List<FaceExpression> faceQueue = new List<FaceExpression>();
        private float faceStartAt { get; set; }
        private FaceExpression currentFace { get; set; }

        private void Awake()
        {
            faceExpressionProxy = gameObject.GetComponent<IFaceExpressionProxy>();
        }

        private void Update()
        {
            UpdateFace();
        }

        public void Setup(GameObject avatarModel)
        {
            faceExpressionProxy.Setup(avatarModel);
        }

#region Face Expression
        // Set face expressions
        public void SetFace(List<FaceExpression> faces)
        {
            faceQueue.Clear();
            faceQueue = new List<FaceExpression>(faces);    // Copy faces not to change original list
            faceStartAt = 0;
        }

        private void UpdateFace()
        {
            // This method will be called every frames in `Update()`
            var faceToSet = GetFaceExpression();
            if (faceToSet == null)
            {
                // Set neutral instead when faceToSet is null
                SetFace(new List<FaceExpression>() { new FaceExpression("Neutral", 0.0f, string.Empty) });
                return;
            }

            if (currentFace == null || faceToSet.Name != currentFace.Name || faceToSet.Duration != currentFace.Duration)
            {
                // Set new face
                faceExpressionProxy.SetExpressionSmoothly(faceToSet.Name, 1.0f);
                currentFace = faceToSet;
                faceStartAt = Time.realtimeSinceStartup;
            }
        }

        public FaceExpression GetFaceExpression()
        {
            if (faceQueue.Count == 0) return default;

            if (faceStartAt > 0 && Time.realtimeSinceStartup - faceStartAt > currentFace.Duration)
            {
                // Remove the first face after the duration passed
                faceQueue.RemoveAt(0);
                faceStartAt = 0;
            }

            if (faceQueue.Count == 0) return default;

            return faceQueue.First();
        }
#endregion
    }
}
