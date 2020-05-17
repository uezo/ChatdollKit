using UnityEngine;
using UnityEditor;
using ChatdollKit.Examples.HelloWorld;


// Put button to start chat
[CustomEditor(typeof(HelloWorld))]
public class HelloWorldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var app = target as HelloWorld;

        if (GUILayout.Button("Start Chat"))
        {
            _ = app.ChatAsync();
        }
    }
}
