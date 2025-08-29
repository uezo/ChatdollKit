using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using uLipSync;

namespace ChatdollKit.Model
{
    public class ConfigurableLipSyncHelper : uLipSyncHelper
    {
        [SerializeField] private string blendShapeNameForMouthA = "vrc.v_aa";
        [SerializeField] private string blendShapeNameForMouthI = "vrc.v_ih";
        [SerializeField] private string blendShapeNameForMouthU = "vrc.v_ou";
        [SerializeField] private string blendShapeNameForMouthE = "vrc.v_e";
        [SerializeField] private string blendShapeNameForMouthO = "vrc.v_oh";

        private string[] BlendShapeNamesForMouth
            => new[] { blendShapeNameForMouthA, blendShapeNameForMouthI, blendShapeNameForMouthU, blendShapeNameForMouthE, blendShapeNameForMouthO };

        public override void ConfigureViseme(GameObject avatarObject)
        {
            // Get BlendShapeMap for viseme
            var blendShapeMap = GetBlendShapeMap(avatarObject);

            if (blendShapeMap == null)
            {
                Debug.LogWarning("Could not find a SkinnedMeshRenderer for facial blendshapes. Please review the Inspector variables: blendShapeNameForMouthA to blendShapeNameForMouthO.");
                return;
            }

            // Get/Add uLipSyncBlendShape
            var uLipSyncBlendShape = gameObject.GetComponent<uLipSyncBlendShape>();
            if (uLipSyncBlendShape == null)
            {
                uLipSyncBlendShape = gameObject.AddComponent<uLipSyncBlendShape>();
            }

            // Configure uLipSyncBlendShape
            uLipSyncBlendShape.skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject, BlendShapeNamesForMouth);

            // Apply blend shapes
            uLipSyncBlendShape.blendShapes.Clear();
            foreach (var map in blendShapeMap)
            {
                uLipSyncBlendShape.blendShapes.Add(new uLipSyncBlendShape.BlendShapeInfo() { phoneme = map.Key, index = map.Value, maxWeight = 1 });
            }

#if UNITY_EDITOR
            // Get/Add uLipSync
            var uLipSyncMain = gameObject.GetComponent<uLipSync.uLipSync>();
            if (uLipSyncMain == null)
            {
                uLipSyncMain = gameObject.AddComponent<uLipSync.uLipSync>();
            }

            // Add listener
            UnityEditor.Events.UnityEventTools.AddPersistentListener(uLipSyncMain.onLipSyncUpdate, uLipSyncBlendShape.OnLipSyncUpdate);

            // Set profile
            var profiles = UnityEditor.AssetDatabase.FindAssets("-Profile-Female");
            if (profiles.Length > 0)
            {
                uLipSyncMain.profile = UnityEditor.AssetDatabase.LoadAssetAtPath<Profile>(UnityEditor.AssetDatabase.GUIDToAssetPath(profiles.First()));
            }
#endif
        }

        protected override Dictionary<string, int> GetBlendShapeMap(GameObject avatarObject)
        {
            var faceMesh = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject, BlendShapeNamesForMouth);

            if (faceMesh == null)
            {
                return null;
            }

            var mesh = faceMesh.sharedMesh;
            var blendShapeMap = new Dictionary<string, int>()
            {
                { "A", 0 }, { "I", 0 }, { "U", 0 }, { "E", 0 }, { "O", 0 }, { "N", -1 }, { "-", -1 }
            };

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains(blendShapeNameForMouthA))
                {
                    blendShapeMap["A"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains(blendShapeNameForMouthI))
                {
                    blendShapeMap["I"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains(blendShapeNameForMouthU))
                {
                    blendShapeMap["U"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains(blendShapeNameForMouthE))
                {
                    blendShapeMap["E"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains(blendShapeNameForMouthO))
                {
                    blendShapeMap["O"] = i;
                }
            }

            return blendShapeMap;
        }
    }
}
