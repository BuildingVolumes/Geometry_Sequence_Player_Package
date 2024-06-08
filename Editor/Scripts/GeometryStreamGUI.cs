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
        SerializedProperty parentTransform;
        SerializedProperty bounds;

        SerializedProperty pointcloudMaterial;
        SerializedProperty meshMaterial;

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
        bool showMoreSettings;

        BoxBoundsHandle boundingBox = new BoxBoundsHandle();

        private void OnEnable()
        {
            parentTransform = serializedObject.FindProperty("parentTransform");
            bounds = serializedObject.FindProperty("drawBounds");
            
            pointcloudMaterial = serializedObject.FindProperty("pointcloudMaterial");
            meshMaterial = serializedObject.FindProperty("meshMaterial");
            
            bufferSize = serializedObject.FindProperty("bufferSize");
            useAllThreads = serializedObject.FindProperty("useAllThreads");
            threadCount = serializedObject.FindProperty("threadCount");

            droppedFrame = serializedObject.FindProperty("frameDropped");
            currentFrame = serializedObject.FindProperty("currentFrameIndex");
            targetFrameTiming = serializedObject.FindProperty("targetFrameTimeMs");
            currentFrameTiming = serializedObject.FindProperty("elapsedMsSinceLastFrame");
            smoothedFPS = serializedObject.FindProperty("smoothedFPS");

            GeometrySequenceStream player = (GeometrySequenceStream)target;

            serializedObject.ApplyModifiedProperties();

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(parentTransform);

            EditorGUILayout.PropertyField(pointcloudMaterial);
            EditorGUILayout.PropertyField(meshMaterial);

            showMoreSettings = EditorGUILayout.Foldout(showMoreSettings, "More Settings");
            if(showMoreSettings)
            {
                EditorGUILayout.PropertyField(bounds, new GUIContent("Geometry draw bounds"));
            }

            showBufferOptions = EditorGUILayout.Foldout(showBufferOptions, "Buffer Options");
            if (showBufferOptions)
            {
                EditorGUILayout.PropertyField(bufferSize);
                EditorGUILayout.PropertyField(useAllThreads);
                EditorGUILayout.PropertyField(threadCount);
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

        /// <summary>
        /// Draw the editable bounding box
        /// </summary>
        protected virtual void OnSceneGUI()
        {
            GeometrySequenceStream stream = target as GeometrySequenceStream;

            boundingBox.center = stream.drawBounds.center + stream.transform.position;
            boundingBox.size = stream.drawBounds.size;

            if (boundingBox.size.x <= 0.01 && boundingBox.size.y <= 0.01 && boundingBox.size.z <= 0.01)
                boundingBox.size = new Vector3(3, 3, 3);

            EditorGUI.BeginChangeCheck();
            boundingBox.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                // record the target object before setting new values so changes can be undone/redone
                Undo.RecordObject(stream, "Change Bounds");

                // copy the handle's updated data back to the target object
                Bounds newBounds = new Bounds();
                newBounds.center = boundingBox.center - stream.transform.position;
                newBounds.size = boundingBox.size;
                stream.drawBounds = newBounds;
            }

        }

    }
}
