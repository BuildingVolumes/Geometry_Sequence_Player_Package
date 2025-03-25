using UnityEditor;
using UnityEngine;
using System;
using UnityEditor.IMGUI.Controls;

namespace BuildingVolumes.Streaming
{

    [CustomEditor(typeof(GeometrySequenceStream))]
    [CanEditMultipleObjects]
    public class GeometryStreamGUI : Editor
    {
        GeometrySequenceStream stream;

        SerializedProperty meshMaterial;
        SerializedProperty materialSlots;
        SerializedProperty customMaterialSlots;
        SerializedProperty pointSize;
        SerializedProperty pointEmission;

        SerializedProperty bufferSize;
        SerializedProperty useAllThreads;
        SerializedProperty threadCount;

        SerializedProperty droppedFrame;
        SerializedProperty currentFrame;
        SerializedProperty targetFrameTiming;
        SerializedProperty currentFrameTiming;
        SerializedProperty smoothedFPS;

        SerializedProperty attachFrameDebugger;
        SerializedProperty frameDebugger;

        bool showInfo;
        bool showBufferOptions;
        bool showMaterialSlots;

        private void OnEnable()
        {
            meshMaterial = serializedObject.FindProperty("meshMaterial");
            materialSlots = serializedObject.FindProperty("materialSlots");
            customMaterialSlots = serializedObject.FindProperty("customMaterialSlots");
            pointSize = serializedObject.FindProperty("pointSize");
            pointEmission = serializedObject.FindProperty("pointEmission");

            bufferSize = serializedObject.FindProperty("bufferSize");
            useAllThreads = serializedObject.FindProperty("useAllThreads");
            threadCount = serializedObject.FindProperty("threadCount");

            droppedFrame = serializedObject.FindProperty("frameDropped");
            currentFrame = serializedObject.FindProperty("lastFrameIndex");
            targetFrameTiming = serializedObject.FindProperty("targetFrameTimeMs");
            currentFrameTiming = serializedObject.FindProperty("lastFrameTime");
            smoothedFPS = serializedObject.FindProperty("smoothedFPS");

            attachFrameDebugger = serializedObject.FindProperty("attachFrameDebugger");
            frameDebugger = serializedObject.FindProperty("frameDebugger");

            stream = (GeometrySequenceStream)target;

            serializedObject.ApplyModifiedProperties();

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();


            GUILayout.Label("Pointcloud Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(pointSize);
            if (EditorGUI.EndChangeCheck())
                stream.SetPointSize(pointSize.floatValue);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Point Type");
            EditorGUI.BeginChangeCheck();
            GeometrySequenceStream.PointType newType = (GeometrySequenceStream.PointType)EditorGUILayout.EnumPopup(stream.pointType);
            if (EditorGUI.EndChangeCheck())
            {
                stream.SetPointcloudMaterial(newType);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(pointEmission);
            if(EditorGUI.EndChangeCheck())
                stream.SetPointEmission(pointEmission.floatValue);

            GUILayout.Space(10);

            GUILayout.Label("Mesh Settings", EditorStyles.boldLabel);

            bool updateMaterial = false;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(meshMaterial);
            showMaterialSlots = EditorGUILayout.Foldout(showMaterialSlots, "Material Slots");
            if (showMaterialSlots)
            {
                EditorGUILayout.PropertyField(materialSlots, new GUIContent("Apply to texture slots: "));
                EditorGUILayout.PropertyField(customMaterialSlots, new GUIContent("Custom texture slots"));
            }
            updateMaterial = EditorGUI.EndChangeCheck();


            showBufferOptions = EditorGUILayout.Foldout(showBufferOptions, "Buffer Options");
            if (showBufferOptions)
            {
                EditorGUILayout.PropertyField(bufferSize);
                if (bufferSize.intValue < 2)
                    bufferSize.intValue = 2;

                EditorGUILayout.PropertyField(useAllThreads);
                if (useAllThreads.boolValue)
                    GUI.enabled = false;
                EditorGUILayout.PropertyField(threadCount);
                GUI.enabled = true;
            }

            showInfo = EditorGUILayout.Foldout(showInfo, "Frame Info");
            if (showInfo)
            {
                EditorGUILayout.LabelField("Currently played frame: " + currentFrame.intValue);
                EditorGUILayout.LabelField("Target frame time in ms:    " + targetFrameTiming.floatValue.ToString("0"));
                EditorGUILayout.LabelField("Current frame time in ms:   " + currentFrameTiming.floatValue.ToString("0"));
                EditorGUILayout.LabelField("Smoothed FPS:   " + smoothedFPS.floatValue.ToString("0"));
                EditorGUILayout.LabelField("Dropped frame:  " + droppedFrame.boolValue);

                GUILayout.Space(10);
                GUILayout.Label("Frame Debugger", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(attachFrameDebugger, new GUIContent("Attach debugger (Editor only)"));
                EditorGUI.BeginDisabledGroup(attachFrameDebugger.boolValue);
                EditorGUILayout.PropertyField(frameDebugger, new GUIContent("Manually attach debugger"));

            }

            serializedObject.ApplyModifiedProperties();

            if(updateMaterial)
                stream.SetMeshMaterial(stream.meshMaterial);
        }
    }
}
