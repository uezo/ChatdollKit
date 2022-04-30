using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace ChatdollKit.Model
{
    // Face clip
    [Serializable]
    public class FaceClip
    {
        [SerializeField]
        public string Name;
        [SerializeField]
        public List<BlendShapeValue> Values;

        public FaceClip()
        {

        }

        // Create FaceClip with passed weigths or current weights of SkinnedMeshRenderer
        public FaceClip(string name, SkinnedMeshRenderer skinnedMeshRenderer, Dictionary<string, float> weights = null)
        {
            Name = name;
            Values = new List<BlendShapeValue>();
            if (skinnedMeshRenderer == null)
            {
                return;
            }
            for (var i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
            {
                var blendShapeName = skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
                var weight = 0.0f;
                if (weights == null)
                {
                    weight = skinnedMeshRenderer.GetBlendShapeWeight(i);
                }
                else
                {
                    weight = weights.ContainsKey(blendShapeName) ? weights[blendShapeName] : weight;
                }
                Values.Add(new BlendShapeValue() { Index = i, Name = blendShapeName, Weight = weight });
            }
        }

        // Get weight by name
        public float GetWeight(string name)
        {
            var blendShapeValue = Values.Where(v => v.Name == name).FirstOrDefault();
            if (blendShapeValue != null)
            {
                return blendShapeValue.Weight;
            }
            else
            {
                Debug.LogWarning($"Blend shape not found: {name}");
                return 0.0f;
            }
        }
    }

    [Serializable]
    public class BlendShapeValue
    {
        [SerializeField]
        public string Name;
        [SerializeField]
        public int Index;
        [SerializeField]
        public float Weight;
    }
}
