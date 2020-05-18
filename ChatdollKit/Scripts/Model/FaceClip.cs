using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace ChatdollKit.Model
{
    // Face clip
    public class FaceClip
    {
        public string Name { get; set; }
        public List<BlendShapeValue> Values { get; set; }

        public FaceClip()
        {

        }

        // Create FaceClip with passed weigths or current weights of SkinnedMeshRenderer
        public FaceClip(string name, SkinnedMeshRenderer skinnedMeshRenderer, Dictionary<string, float> weights = null)
        {
            Name = name;
            Values = new List<BlendShapeValue>();
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

    public class BlendShapeValue
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public float Weight { get; set; }
    }
}
