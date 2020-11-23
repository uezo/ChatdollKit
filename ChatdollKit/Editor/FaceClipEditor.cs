using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;
using Newtonsoft.Json;
using ChatdollKit.Model;

[CustomEditor(typeof(ModelController))]
public class FaceClipEditor : Editor
{
    // For face configuration
    private string previousConfigName;
    private string currentConfigName;
    private List<FaceClip> faceClips;
    private int previousSelectedFaceIndex;
    private int selectedFaceIndex;
    private string currentFaceName;

    // For setup
    private static string[] visemeNames = new string[] {
            "sil", "PP", "FF", "TH", "DD",
            "kk", "CH", "SS", "nn", "RR",
            "aa", "E", "ih", "oh", "ou" };

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var modelController = target as ModelController;
        var skinnedMeshRenderer = modelController.SkinnedMeshRenderer;
        var ButtonLayout = new GUILayoutOption[] { GUILayout.Width(80) };

        if (skinnedMeshRenderer != null)
        {
            if (modelController.FaceClipConfiguration == null)
            {
                // Clear local list
                faceClips = null;

                if (GUILayout.Button("Import JSON (migration)"))
                {
                    LoadFacesFromJson();
                }

                return;
            }

            previousConfigName = currentConfigName;
            currentConfigName = modelController.FaceClipConfiguration.name;

            // Reset flag to determine selection changed
            var selectionChanged = false;

            // Load data on started or configuration changed
            if (faceClips == null || currentConfigName != previousConfigName)
            {
                faceClips = modelController.FaceClipConfiguration.FaceClips;
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
                    EditorUtility.SetDirty(modelController.FaceClipConfiguration);
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
                    var faceToUpdate = faceClips.Where(f => f.Name == currentFaceName).FirstOrDefault();
                    if (faceToUpdate == null)
                    {
                        faceClips.Add(new FaceClip(currentFaceName, skinnedMeshRenderer));
                    }
                    else
                    {
                        faceToUpdate = new FaceClip(currentFaceName, skinnedMeshRenderer);
                    }

                    // Select item
                    selectedFaceIndex = faceClips.Select(f => f.Name).ToList().IndexOf(currentFaceName);

                    // Save to asset
                    EditorUtility.SetDirty(modelController.FaceClipConfiguration);
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

    // Migrate face configuration from JSON
    public void LoadFacesFromJson()
    {
        var path = EditorUtility.OpenFilePanel("Select FaceClip configuration", "", "json");
        if (path.Length == 0)
        {
            return;
        }

        try
        {
            var modelController = target as ModelController;

            // Load faces from JSON
            var faces = GetFacesFromFile(path);

            // Create ScriptableObject and set face clips
            var faceClipConfiguration = CreateInstance<FaceClipConfiguration>();
            modelController.FaceClipConfiguration = faceClipConfiguration;
            modelController.FaceClipConfiguration.FaceClips = faces;

            // Create face configuration asset
            AssetDatabase.CreateAsset(
                faceClipConfiguration,
                $"Assets/Resources/Faces-{modelController.gameObject.name}-{DateTime.Now.ToString("yyyyMMddHHmmSS")}.asset");
            EditorUtility.SetDirty(modelController.FaceClipConfiguration);
            AssetDatabase.SaveAssets();

            // Set face clips to local list
            faceClips = modelController.FaceClipConfiguration.FaceClips;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import face clips from JSON: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Get faces from JSON file
    private List<FaceClip> GetFacesFromFile(string path)
    {
        var storedFaces = new List<FaceClip>();

        var faceJsonString = File.ReadAllText(path);
        if (faceJsonString != null)
        {
            storedFaces = JsonConvert.DeserializeObject<List<FaceClip>>(faceJsonString);
        }

        return storedFaces;
    }

    // Setup ModelController
    [MenuItem("CONTEXT/ModelController/Setup ModelController")]
    private static void Setup(MenuCommand menuCommand)
    {
        var modelController = menuCommand.context as ModelController;

        // Get viseme target as VRC FBX or VRM
        var skinnedMeshRenderers = modelController.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        var visemeTarget = GetVisemeTarget(skinnedMeshRenderers, false) ?? GetVisemeTarget(skinnedMeshRenderers, true);

        if (visemeTarget == null)
        {
            Debug.LogError("Viseme target not found");
            return;
        }

        // Configure LipSync viseme
        var lipSyncObject = ConfigureViseme(modelController.gameObject, visemeTarget);

        // Set face skinnedMeshRenderer
        modelController.SkinnedMeshRenderer = visemeTarget.SkinnedMeshRenderer;

        // Set and configure audio source
        modelController.AudioSource = lipSyncObject.GetComponent<AudioSource>();
        modelController.AudioSource.playOnAwake = false;

        // Set LipSyncContext
        modelController.LipSyncContext = lipSyncObject.GetComponent<OVRLipSyncContext>();

        // Set blink target
        modelController.BlinkBlendShapeName = GetBlinkTargetName(modelController.SkinnedMeshRenderer);

        // Create and set face configuration
        var faceClipConfiguration = CreateInstance<FaceClipConfiguration>();
        AssetDatabase.CreateAsset(
            faceClipConfiguration,
            $"Assets/Resources/Faces-{modelController.gameObject.name}-{DateTime.Now.ToString("yyyyMMddHHmmSS")}.asset");
        modelController.FaceClipConfiguration = faceClipConfiguration;
    }

    // Setup Animator
    [MenuItem("CONTEXT/ModelController/Setup Animator")]
    private static void CreateAnimationControllerWithClips(MenuCommand menuCommand)
    {
        var animationClipFolderPath = EditorUtility.OpenFolderPanel("Select animation clip parent folder", Application.dataPath, string.Empty);

        if (!string.IsNullOrEmpty(animationClipFolderPath))
        {
            // Get animation clips from folder
            var animationClips = GetLayeredAnimationClips(animationClipFolderPath);

            // Return when no clips found
            if (animationClips.Count == 0)
            {
                Debug.LogWarning("No animation clips found");
                return;
            }

            // Make path to create new animator controller
            var animatorControllerPath = "Assets" + animationClipFolderPath.Replace(Application.dataPath, string.Empty);
            animatorControllerPath = Path.Combine(animatorControllerPath, "ChatdollAnimatorController.controller");

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animatorControllerPath) != null)
            {
                // Confirm overwrite when exists
                if (!EditorUtility.DisplayDialog("AnimatorController exists", $"AnimatorController already exists at {animatorControllerPath}. Are you sure to overwrite?", "OK", "Cancel"))
                {
                    return;
                }
            }

            // Create new animator controller
            var animatorController = AnimatorController.CreateAnimatorControllerAtPath(animatorControllerPath);

            foreach (var kv in animationClips)
            {
                // Select layer
                var layerName = kv.Key;
                var putOnBaseLayer = layerName == "Base layer" || EditorUtility.DisplayDialog("Select Layer", $"{kv.Value.Count} clips found in {layerName}. Put these clips on ...", "Base Layer", $"{layerName}");
                if (!putOnBaseLayer)
                {
                    animatorController.AddLayer(layerName);
                }
                var layer = putOnBaseLayer ? animatorController.layers[0] : animatorController.layers.Last();

                // Create default state
                if (!layer.stateMachine.states.Select(st => st.state.name).Contains("Default"))
                {
                    var defaultState = layer.stateMachine.AddState("Default");
                    if (putOnBaseLayer && kv.Value.Count > 0)
                    {
                        defaultState.motion = kv.Value[0];
                    }

                }

                // Put animation clips on layer
                foreach (var clip in kv.Value)
                {
                    var state = layer.stateMachine.AddState(clip.name);
                    state.motion = clip;
                }
            }

            // Set controller to animator
            var modelController = menuCommand.context as ModelController;
            var animator = modelController.gameObject.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = animatorController;
            }
        }
    }

    private static Dictionary<string, List<AnimationClip>> GetLayeredAnimationClips(string parentPath)
    {
        var animationClips = new Dictionary<string, List<AnimationClip>>();

        var directories = Directory.GetDirectories(parentPath);
        if (directories.Length > 0)
        {
            foreach (var d in directories)
            {
                var clips = GetAnimationClips(d);
                if (clips.Count > 0)
                {
                    animationClips[d.Split('/').Last()] = clips;
                }
            }
        }
        else
        {
            var clips = GetAnimationClips(parentPath);
            if (clips.Count > 0)
            {
                animationClips["Base Layer"] = clips;
            }
        }

        return animationClips;
    }

    private static List<AnimationClip> GetAnimationClips(string folderPath)
    {
        var clips = new List<AnimationClip>();
        var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
        foreach (var f in files)
        {
            var assetPath = "Assets" + f.Replace(Application.dataPath, string.Empty);
            var a = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (a != null)
            {
                clips.Add(a);
            }
        }
        return clips;
    }

    private static VisemeTarget GetVisemeTarget(SkinnedMeshRenderer[] skinnedMeshRenderers, bool IsVRM = false)
    {
        var maxCount = 1;
        var maxIndex = -1;
        var visemeTargetShapeKeyIndexes = new List<int>();

        for (var i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            var temp = GetVisemeTargetShapeKeyIndexes(skinnedMeshRenderers[i].sharedMesh, visemeNames, IsVRM);
            if (temp == null) continue;
            var configuredCount = temp.Where(v => v >= 0).Count();
            if (configuredCount >= maxCount)
            {
                maxCount = configuredCount;
                maxIndex = i;
                visemeTargetShapeKeyIndexes = temp;
            }
        }

        if (maxIndex < 0)
        {
            // Return when no target indexes
            return null;
        }
        else
        {
            return new VisemeTarget()
            {
                SkinnedMeshRenderer = skinnedMeshRenderers[maxIndex],
                ShapeKeyIndexes = visemeTargetShapeKeyIndexes
            };
        }
    }

    private static List<int> GetVisemeTargetShapeKeyIndexes(Mesh mesh, string[] visemeNames, bool IsVRM = false)
    {
        var visemeTargetShapeKeyIndexes = new List<int>();

        // Get shapekeys
        var shapeKeys = new Dictionary<string, int>();
        for (var i = 0; i < mesh.blendShapeCount; i++)
        {
            shapeKeys[mesh.GetBlendShapeName(i)] = i;
        }

        // Set index of shapekeys
        for (var i = 0; i < visemeNames.Length; i++)
        {
            var visemeKey = visemeNames[i];
            if (IsVRM)
            {
                if (visemeKey.ToLower() == "aa") visemeKey = "a";
                else if (visemeKey.ToLower() == "ih") visemeKey = "i";
                else if (visemeKey.ToLower() == "ou") visemeKey = "u";
                else if (visemeKey.ToLower() == "e") visemeKey = "e";
                else if (visemeKey.ToLower() == "oh") visemeKey = "o";
                else visemeKey = "n";
            }

            var shapeKey = shapeKeys.Keys.Where(sk => sk.ToLower().Trim().EndsWith($"_{visemeKey}".ToLower())).FirstOrDefault();
            if (!string.IsNullOrEmpty(shapeKey))
            {
                visemeTargetShapeKeyIndexes.Add(shapeKeys[shapeKey]);
            }
            else
            {
                return null;
            }
        }

        return visemeTargetShapeKeyIndexes;
    }

    private static GameObject ConfigureViseme(GameObject rootGameObject, VisemeTarget visemeTarget)
    {
        var morphTarget = rootGameObject.GetComponentInChildren<OVRLipSyncContextMorphTarget>();
        if (morphTarget == null)
        {
            var morphTargetGameObject = new GameObject("LipSync");
            morphTarget = morphTargetGameObject.AddComponent<OVRLipSyncContextMorphTarget>();
            morphTarget.transform.parent = visemeTarget.SkinnedMeshRenderer.gameObject.transform;
        }

        morphTarget.skinnedMeshRenderer = visemeTarget.SkinnedMeshRenderer;
        for (var i = 0; i < visemeNames.Length; i++)
        {
            if (visemeTarget.ShapeKeyIndexes[i] >= 0)
            {
                morphTarget.visemeToBlendTargets[i] = visemeTarget.ShapeKeyIndexes[i];
            }
        }

        var context = morphTarget.gameObject.GetComponent<OVRLipSyncContext>();
        if (context == null)
        {
            context = morphTarget.gameObject.AddComponent<OVRLipSyncContext>();
        }
        context.audioLoopback = true;

        return morphTarget.gameObject;
    }

    private static string GetBlinkTargetName(SkinnedMeshRenderer skinnedMeshRenderer)
    {
        var mesh = skinnedMeshRenderer.sharedMesh;
        for (var i = 0; i < mesh.blendShapeCount; i++)
        {
            var shapeName = mesh.GetBlendShapeName(i).ToLower();
            if (!shapeName.Contains("left") && !shapeName.Contains("right"))
            {
                if (shapeName.Contains("blink") || (shapeName.Contains("eye") && shapeName.Contains("close")))
                {
                    return shapeName;
                }
            }
        }

        return string.Empty;
    }

    class VisemeTarget
    {
        public SkinnedMeshRenderer SkinnedMeshRenderer { get; set; }
        public List<int> ShapeKeyIndexes { get; set; }
    }
}
