using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using ChatdollKit.Model;


[CustomEditor(typeof(ModelController))]
public class FaceClipEditor : Editor
{
    private List<FaceClip> faceClips;
    private int previousSelectedFaceIndex;
    private int selectedFaceIndex;
    private string currentFaceName;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var modelController = target as ModelController;
        var skinnedMeshRenderer = modelController.SkinnedMeshRenderer;
        var ButtonLayout = new GUILayoutOption[] { GUILayout.Width(80) };

        if (skinnedMeshRenderer != null)
        {
            if (string.IsNullOrEmpty(modelController.FaceConfigurationFile) || !File.Exists(modelController.FaceConfigurationFile))
            {
                EditorGUILayout.HelpBox("Create new face configuration file to use face capture tool.", MessageType.Info, true);
                if (GUILayout.Button("Create"))
                {
                    try
                    {
                        var newFilePath = EditorUtility.SaveFilePanel("Create new face configuration file", Application.dataPath, "faceConfig", "json");
                        if (!string.IsNullOrEmpty(newFilePath))
                        {
                            File.WriteAllText(newFilePath, JsonConvert.SerializeObject(new List<FaceClip>()));
                            modelController.FaceConfigurationFile = newFilePath;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to create face configuration file: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                return;
            }

            var path = modelController.FaceConfigurationFile;

            // Reset flag to determine selection changed
            var selectionChanged = false;

            // Initial load
            if (faceClips == null)
            {
                faceClips = GetFacesFromFile(path);
                if (faceClips.Count > 0)
                {
                    selectionChanged = true;
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
                if (faceClips.Count > 0 && RemoveFace(path, faceClips[selectedFaceIndex].Name))
                {
                    if (faceClips.Count == 0)
                    {
                        // nothing to be selected when no item exists
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
                    // Update and save FaceClip with current weights of SkinnedMeshRenderer
                    if (UpdateFace(path, new FaceClip(currentFaceName, skinnedMeshRenderer)))
                    {
                        // Change selected index to new item
                        selectedFaceIndex = faceClips.Select(f => f.Name).ToList().IndexOf(currentFaceName);
                    }
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

    // Update face
    private bool UpdateFace(string path, FaceClip face)
    {
        try
        {
            // Add or update face to JSON
            var storedFaces = GetFacesFromFile(path);
            var faceToUpdate = storedFaces.Where(f => f.Name == face.Name).FirstOrDefault();
            if (faceToUpdate == null)
            {
                storedFaces.Add(face);
            }
            else
            {
                faceToUpdate.Values = face.Values;
            }

            // Save face list
            File.WriteAllText(path, JsonConvert.SerializeObject(storedFaces));

            // Refresh face list on memory
            faceClips.Clear();
            faceClips = storedFaces;

            // Return true when done successfully
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to update face clip: {ex.Message}\n{ex.StackTrace}");
        }

        return false;
    }

    // Remove face
    private bool RemoveFace(string path, string faceName)
    {
        try
        {
            // Remove face from JSON
            var storedFaces = GetFacesFromFile(path);
            var faceToRemove = storedFaces.Where(f => f.Name == faceName).First();
            storedFaces.Remove(faceToRemove);

            // Save removed face list
            File.WriteAllText(path, JsonConvert.SerializeObject(storedFaces));

            // Refresh face list on memory
            faceClips.Clear();
            faceClips = storedFaces;

            // Return true when done successfully
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to remove face clip: {ex.Message}\n{ex.StackTrace}");
        }

        // Return false when error occured
        return false;
    }

    // Get faces from JSON file
    private List<FaceClip> GetFacesFromFile(string path)
    {
        var storedFaces = new List<FaceClip>();

        try
        {
            var facesJson = File.ReadAllText(path);
            storedFaces = JsonConvert.DeserializeObject<List<FaceClip>>(facesJson);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get face clips from file: {ex.Message}\n{ex.StackTrace}");
        }

        return storedFaces;
    }
}