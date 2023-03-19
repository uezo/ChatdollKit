using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using uLipSync;

namespace ChatdollKit.Model
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
            var uLipSyncBlendShape = gameObject.GetComponent<uLipSyncBlendShape>();

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
                { "A", 0 }, { "I", 0 }, { "U", 0 }, { "E", 0 }, { "O", 0 }, { "N", -1 }, { "-", -1 }
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
