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

        SerializedProperty pointcloudMaterial;
        SerializedProperty meshMaterial;
        SerializedProperty materialSlots;
        SerializedProperty customMaterialSlots;
        SerializedProperty pointSize;
        SerializedProperty pointType;

        SerializedProperty bufferSize;
        SerializedProperty useAllThreads;
        SerializedProperty threadCount;

        SerializedProperty droppedFrame;
        SerializedProperty currentFrame;
        SerializedProperty targetFrameTiming;
        SerializedProperty currentFrameTiming;
        SerializedProperty smoothedFPS;

        bool showInfo;
        bool showBufferOptions;
        bool showMaterialSlots;

        private void OnEnable()
        {            
            meshMaterial = serializedObject.FindProperty("meshMaterial");
            materialSlots = serializedObject.FindProperty("materialSlots");
            customMaterialSlots = serializedObject.FindProperty("customMaterialSlots");
            pointSize = serializedObject.FindProperty("pointSize");
            pointType = serializedObject.FindProperty("pointType");

            bufferSize = serializedObject.FindProperty("bufferSize");
            useAllThreads = serializedObject.FindProperty("useAllThreads");
            threadCount = serializedObject.FindProperty("threadCount");

            droppedFrame = serializedObject.FindProperty("frameDropped");
            currentFrame = serializedObject.FindProperty("currentFrameIndex");
            targetFrameTiming = serializedObject.FindProperty("targetFrameTimeMs");
            currentFrameTiming = serializedObject.FindProperty("elapsedMsSinceLastFrame");
            smoothedFPS = serializedObject.FindProperty("smoothedFPS");

            stream = (GeometrySequenceStream)target;

            serializedObject.ApplyModifiedProperties();

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();


            GUILayout.Label("Pointcloud Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(pointSize);
            if(EditorGUI.EndChangeCheck())
                stream.SetPointSize(pointSize.floatValue);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Point Type");
            EditorGUI.BeginChangeCheck();
            GeometrySequenceStream.PointType newType = (GeometrySequenceStream.PointType)EditorGUILayout.EnumPopup(stream.pointType);
            if (EditorGUI.EndChangeCheck())
            {
                MeshRenderer activeRend = stream.GetActiveRenderer();
                if (activeRend != null)
                    stream.SetPointcloudMaterial(newType, activeRend);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.Label("Mesh Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(meshMaterial);
            showMaterialSlots = EditorGUILayout.Foldout(showMaterialSlots, "Material Slots");
            if (showMaterialSlots) 
            {
                EditorGUILayout.PropertyField(materialSlots, new GUIContent("Apply to texture slots: "));
                EditorGUILayout.PropertyField(customMaterialSlots, new GUIContent("Custom texture slots"));
            }


            showBufferOptions = EditorGUILayout.Foldout(showBufferOptions, "Buffer Options");
            if (showBufferOptions)
            {
                EditorGUILayout.PropertyField(bufferSize);
                EditorGUILayout.PropertyField(useAllThreads);
                if (useAllThreads.boolValue)
                    GUI.enabled = false;
                EditorGUILayout.PropertyField(threadCount);
                GUI.enabled = true;
            }

            showInfo = EditorGUILayout.Foldout(showInfo, "Frame Info");
            if (showInfo)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(droppedFrame, new GUIContent("Dropped Frame"));
                EditorGUILayout.PropertyField(currentFrame, new GUIContent("Currently played frame"));
                EditorGUILayout.PropertyField(targetFrameTiming, new GUIContent("Target frame time in ms"));
                EditorGUILayout.PropertyField(currentFrameTiming, new GUIContent("Current frame time in ms"));
                EditorGUILayout.PropertyField(smoothedFPS, new GUIContent("Smoothed FPS"));
                EditorGUI.EndDisabledGroup();
            }           

            serializedObject.ApplyModifiedProperties();
        }
    }
}
