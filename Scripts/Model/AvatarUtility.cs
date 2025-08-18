using UnityEngine;

namespace ChatdollKit.Model
{
    public class AvatarUtility
    {
        public static SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(GameObject avatarGameObject, string blendShapeKeyword = null)
        {
            string[] blendShapeKeywords = { "blink", "eye_close", blendShapeKeyword };

            foreach (var smr in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    var blendShapeNameLower = smr.sharedMesh.GetBlendShapeName(i).ToLower();

                    if (blendShapeKeywords.Any(keyword => !string.IsNullOrEmpty(keyword) && blendShapeNameLower.Contains(keyword)))
                    {
                        return smr;
                    }
                }
            }

            Debug.LogWarning("Facial SkinnedMeshRenderer not found");
            return null;
        }
    }
}
