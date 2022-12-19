using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ChatdollKit.Model;
using uLipSync;

namespace ChatdollKit.Extension.uLipSyncEx
{
    public class uLipSyncHelper : MonoBehaviour, ILipSyncHelper
    {
        public void ResetViseme()
        {
            // do nothing
        }

#if UNITY_EDITOR
        public void ConfigureViseme()
        {
            // Get GameObjects
            var modelController = gameObject.GetComponent<ModelController>();
            if (modelController == null || modelController.AudioSource == null)
            {
                Debug.LogError("Add and setup ModelController before. You can retry setting up LipSync by selecting `Reset` in the context menu of OVRLipSyncHelper.");
                return;
            }

            // Get/Add uLipSyncBlendShape
            var uLipSyncBlendShape = gameObject.GetComponent<uLipSyncBlendShape>();
            if (uLipSyncBlendShape == null)
            {
                uLipSyncBlendShape = gameObject.AddComponent<uLipSyncBlendShape>();
            }

            // Configure uLipSyncBlendShape
            uLipSyncBlendShape.skinnedMeshRenderer = modelController.SkinnedMeshRenderer;

            // Map blend shapes
            Dictionary<string, int> blendShapeMap = null;
            if (IsVRCModel(uLipSyncBlendShape.skinnedMeshRenderer.sharedMesh))
            {
                blendShapeMap = GetVRCBlendShapeMap(uLipSyncBlendShape.skinnedMeshRenderer.sharedMesh);
            }
            else if (IsVRMModel(uLipSyncBlendShape.skinnedMeshRenderer.sharedMesh))
            {
                blendShapeMap = GetVRMBlendShapeMap(uLipSyncBlendShape.skinnedMeshRenderer.sharedMesh);
            }
            else
            {
                Debug.LogWarning("BlendShapes for VRC or VRM are not found. Set visemes manually to use LipSync.");
            }

            // Apply blend shapes
            uLipSyncBlendShape.blendShapes.Clear();
            foreach (var map in blendShapeMap)
            {
                uLipSyncBlendShape.blendShapes.Add(new uLipSyncBlendShape.BlendShapeInfo() { phoneme = map.Key, index = map.Value, maxWeight = 1 });
            }

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
        }

        private Dictionary<string, int> GetVRCBlendShapeMap(Mesh mesh)
        {
            var blendShapeMap = GetBlendShapeMapBase();

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains("vrc.v_aa"))
                {
                    blendShapeMap["A"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains("vrc.v_ih"))
                {
                    blendShapeMap["I"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains("vrc.v_ou"))
                {
                    blendShapeMap["U"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains("vrc.v_e"))
                {
                    blendShapeMap["E"] = i;
                }
                else if (mesh.GetBlendShapeName(i).Contains("vrc.v_oh"))
                {
                    blendShapeMap["O"] = i;
                }
            }

            return blendShapeMap;
        }

        private Dictionary<string, int> GetVRMBlendShapeMap(Mesh mesh)
        {
            var blendShapeMap = GetBlendShapeMapBase();

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_A"))
                {
                    blendShapeMap["A"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_I"))
                {
                    blendShapeMap["I"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_U"))
                {
                    blendShapeMap["U"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_E"))
                {
                    blendShapeMap["E"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_O"))
                {
                    blendShapeMap["O"] = i;
                }
            }

            return blendShapeMap;
        }

        private Dictionary<string, int> GetBlendShapeMapBase()
        {
            return new Dictionary<string, int>()
            {
                { "A", 0 }, { "I", 0 }, { "U", 0 }, { "E", 0 }, { "O", 0 }, { "N", 0 }, { "-", 0 }
            };
        }

        private bool IsVRCModel(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains("vrc.v_"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsVRMModel(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains("Fcl_MTH_"))
                {
                    return true;
                }
            }

            return false;
        }
#endif
    }
}
