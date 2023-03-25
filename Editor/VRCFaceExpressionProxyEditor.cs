using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using ChatdollKit.Model;

[CustomEditor(typeof(VRCFaceExpressionProxy))]
public class VRCFaceExpressionProxyEditor : Editor
{
    private string previousConfigName;
    private string currentConfigName;
    private List<FaceClip> faceClips;
    private int previousSelectedFaceIndex;
    private int selectedFaceIndex;
    private string currentFaceName;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var proxy = target as VRCFaceExpressionProxy;
        var skinnedMeshRenderer = proxy.SkinnedMeshRenderer;
        var ButtonLayout = new GUILayoutOption[] { GUILayout.Width(80) };

        if (skinnedMeshRenderer != null)
        {
            if (proxy.FaceClipConfiguration == null)
            {
                // Clear local list
                faceClips = null;
                return;
            }

            previousConfigName = currentConfigName;
            currentConfigName = proxy.FaceClipConfiguration.name;

            // Reset flag to determine selection changed
            var selectionChanged = false;

            // Load data on started or configuration changed
            if (faceClips == null || currentConfigName != previousConfigName)
            {
                faceClips = proxy.FaceClipConfiguration.FaceClips;
                if (faceClips.Count > 0)
                {
                    selectionChanged = true;
                    selectedFaceIndex = 0;
                }
                else
                {
                    selectedFaceIndex = -1;
                    currentFaceName = string.Empty;
                }
            }

            EditorGUILayout.BeginHorizontal();

            // Pulldown selection
            previousSelectedFaceIndex = selectedFaceIndex;
            selectedFaceIndex = EditorGUILayout.Popup(selectedFaceIndex, faceClips.Select(f => new GUIContent(f.Name)).ToArray());
            if (faceClips.Count > 0 && previousSelectedFaceIndex != selectedFaceIndex)
            {
                selectionChanged = true;
            }

            // Remove face
            if (GUILayout.Button("Remove", ButtonLayout))
            {
                if (faceClips.Count > 0)
                {
                    // Remove from list
                    var faceToRemove = faceClips.Where(f => f.Name == currentFaceName).First();
                    faceClips.Remove(faceToRemove);

                    // Save to asset
                    EditorUtility.SetDirty(proxy.FaceClipConfiguration);
                    AssetDatabase.SaveAssets();

                    // Select item
                    if (faceClips.Count == 0)
                    {
                        // Nothing to be selected when no item exists
                        selectedFaceIndex = -1;
                        currentFaceName = string.Empty;
                    }
                    else
                    {
                        if (selectedFaceIndex == 0)
                        {
                            selectedFaceIndex = 0;
                        }
                        else
                        {
                            selectedFaceIndex--;
                        }
                        selectionChanged = true;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Add or update face
            EditorGUILayout.BeginHorizontal();
            currentFaceName = GUILayout.TextField(currentFaceName);
            if (GUILayout.Button("Capture", ButtonLayout))
            {
                if (!string.IsNullOrEmpty(currentFaceName.Trim()))
                {
                    // Update or add to list
                    var faceIndexToUpdate = faceClips.Select(f => f.Name).ToList().IndexOf(currentFaceName);
                    if (faceIndexToUpdate > -1)
                    {
                        faceClips[faceIndexToUpdate] = new FaceClip(currentFaceName, skinnedMeshRenderer);
                    }
                    else
                    {
                        faceClips.Add(new FaceClip(currentFaceName, skinnedMeshRenderer));
                    }

                    // Select item
                    selectedFaceIndex = faceClips.Select(f => f.Name).ToList().IndexOf(currentFaceName);

                    // Save to asset
                    proxy.FaceClipConfiguration.FaceClips = faceClips;
                    EditorUtility.SetDirty(proxy.FaceClipConfiguration);
                    AssetDatabase.SaveAssets();
                }
            }
            EditorGUILayout.EndHorizontal();

            // Change current name and blend shapes when selection changed
            if (selectionChanged)
            {
                currentFaceName = faceClips[selectedFaceIndex].Name;
                foreach (var blendShapeValue in faceClips[selectedFaceIndex].Values)
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(blendShapeValue.Index, blendShapeValue.Weight);
                }
            }
        }
    }

    [MenuItem("CONTEXT/VRCFaceExpressionProxy/Setup VRC FaceExpression Proxy")]
    private static void Setup(MenuCommand menuCommand)
    {
        var proxy = menuCommand.context as VRCFaceExpressionProxy;

        if (proxy.SkinnedMeshRenderer == null)
        {
            proxy.SkinnedMeshRenderer = proxy.gameObject.GetComponent<ModelController>().SkinnedMeshRenderer;
        }

        if (proxy.FaceClipConfiguration == null)
        {
            // Get blink blend shape key
            var blinkBlendShapeWeights = new Dictionary<string, float>();
            var blink = proxy.gameObject.GetComponent<Blink>();
            if (blink != null)
            {
                blinkBlendShapeWeights.Add(blink.GetBlinkShapeName(), 100.0f);
            }

            // Create new FaceClipConfiguration and add faces with empty values
            var faceClipConfiguration = CreateInstance<FaceClipConfiguration>();
            faceClipConfiguration.FaceClips.Add(new FaceClip("Neutral", proxy.SkinnedMeshRenderer, new Dictionary<string, float>()));
            faceClipConfiguration.FaceClips.Add(new FaceClip("Blink", proxy.SkinnedMeshRenderer, blinkBlendShapeWeights));
            faceClipConfiguration.FaceClips.Add(new FaceClip("Joy", proxy.SkinnedMeshRenderer, new Dictionary<string, float>()));
            faceClipConfiguration.FaceClips.Add(new FaceClip("Angry", proxy.SkinnedMeshRenderer, new Dictionary<string, float>()));
            faceClipConfiguration.FaceClips.Add(new FaceClip("Sorrow", proxy.SkinnedMeshRenderer, new Dictionary<string, float>()));
            faceClipConfiguration.FaceClips.Add(new FaceClip("Fun", proxy.SkinnedMeshRenderer, new Dictionary<string, float>()));

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            AssetDatabase.CreateAsset(
                faceClipConfiguration,
                $"Assets/Resources/Faces-{proxy.gameObject.GetComponent<ModelController>().AvatarModel.gameObject.name}-{DateTime.Now.ToString("yyyyMMddHHmmss")}.asset");
            proxy.FaceClipConfiguration = faceClipConfiguration;
        }
        EditorUtility.SetDirty(proxy);
    }
}
