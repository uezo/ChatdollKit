using UnityEngine;

namespace ChatdollKit.Model
{
    public class AvatarUtility
    {
        public static SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(GameObject avatarGameObject)
        {
            foreach (var smr in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    var blendShapeNameLower = smr.sharedMesh.GetBlendShapeName(i).ToLower();

                    if (blendShapeNameLower.Contains("blink") || blendShapeNameLower.Contains("eye_close"))
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
