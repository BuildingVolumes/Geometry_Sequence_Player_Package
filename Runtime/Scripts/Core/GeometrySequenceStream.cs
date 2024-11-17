using UnityEngine;
using System.IO;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BuildingVolumes.Streaming
{
    public class GeometrySequenceStream : MonoBehaviour
    {
        public string pathToSequence { get; private set; }

        public Bounds drawBounds = new Bounds(Vector3.zero, new Vector3(3, 3, 3));

        public int bufferSize = 30;
        public bool useAllThreads = true;
        public int threadCount = 4;

        public bool attachFrameDebugger = false;
        public GSFrameDebugger frameDebugger = null;
        public Material pointcloudMaterialQuads;
        public Material pointcloudMaterialCircles;
        public Material pointcloudMaterialSplats;
        public Material meshMaterial;
        public MaterialProperties materialSlots = MaterialProperties.Albedo;
        public List<string> customMaterialSlots;

        public float pointSize = 0.02f;
        public PointType pointType;

        public PointcloudRenderer pointcloudRenderer;

        MeshFilter thumbnailMeshFilter;
        MeshRenderer thumbnailMeshRenderer;
        PointcloudRenderer thumbnailPCRenderer;
        BufferedGeometryReader thumbnailReader;
        Texture2D thumbnailTexture;

        public bool readerInitialized = false;

        public bool frameDropped = false;
        public int framesDroppedCounter = 0;

        public int lastFrameBufferIndex = 0;
        public float targetFrameTimeMs = 0;
        public float lastFrameTime = 0;
        public int lastFrameIndex;
        public float sequenceDeltaTime = 0;
        public float elapsedMsSinceSequenceStart = 0;
        public float smoothedFPS = 0f;

        float sequenceStartTime = 0;
        float lastSequenceCompletionTime;

        public GameObject streamedMeshObject;
        [HideInInspector]
        public BufferedGeometryReader bufferedReader;
        [HideInInspector]
        public MeshFilter streamedMeshFilter;
        [HideInInspector]
        public MeshRenderer streamedMeshRenderer;
        [HideInInspector]
        public Texture2D texture;

        public enum PathType { AbsolutePath, RelativeToDataPath, RelativeToPersistentDataPath, RelativeToStreamingAssets };
        public enum PointType { Quads, Circles, SplatsExperimental};

        [Flags] public enum MaterialProperties { Albedo = 1, Emission = 2, Detail = 4 }

        private void Awake()
        {

#if !UNITY_EDITOR && !UNITY_STANDALONE_WIN && !UNITY_STANDALONE_OSX && !UNITY_STANDALONE_LINUX && !UNITY_IOS && !UNITY_ANDROID && !UNITY_TVOS
            Debug.LogError("Platform not supported by Geometry Sequence Streamer! Playback will probably fail");
#endif

#if UNITY_VISIONOS
	Debug.LogError("Visions OS is only supported in the Unity Asset Store Version of this plugin!");
#endif

            if (!useAllThreads)
                JobsUtility.JobWorkerCount = threadCount;
            else
                JobsUtility.JobWorkerCount = JobsUtility.JobWorkerMaximumCount;

            if (GetComponent<MeshRenderer>())
                GetComponent<MeshRenderer>().enabled = false;

            SetupMaterials();

            if(attachFrameDebugger)
            {
                AttachFrameDebugger();
            }
            
        }

        /// <summary>
        /// Cleans up the current sequence and prepares the playback of the sequence in the given folder. Doesn't start playback!
        /// </summary>
        /// <param name="absolutePathToSequence">The absolute path to the folder containing a sequence of .ply geometry files and optionally .dds texture files</param>
        public bool ChangeSequence(string absolutePathToSequence, float playbackFPS)
        {
            CleanupSequence();
            lastFrameIndex = -1;
            lastFrameBufferIndex = -1;

            pathToSequence = absolutePathToSequence;

            if (!CreateStreamObject())
                return readerInitialized;

            bufferedReader = new BufferedGeometryReader(streamedMeshObject, meshMaterial);

            if (!bufferedReader.SetupReader(pathToSequence, bufferSize))
                return readerInitialized;

            ConfigureRenderObject(streamedMeshObject, bufferedReader.sequenceConfig, false, out streamedMeshRenderer, out streamedMeshFilter, out pointcloudRenderer, out texture);

            //If we have a single texture in the sequence, we read it immidiatly
            if (bufferedReader.sequenceConfig.textureMode == SequenceConfiguration.TextureMode.Single)
            {
                    bufferedReader.SetupFrameForReading(bufferedReader.frameBuffer[0], bufferedReader.sequenceConfig, 0);
                    bufferedReader.ScheduleTextureReadJob(bufferedReader.frameBuffer[0], bufferedReader.GetDeviceDependendentTexturePath(0));
                    ShowTextureData(bufferedReader.frameBuffer[0], streamedMeshRenderer, texture);
            }

            targetFrameTimeMs = 1000f / (float)playbackFPS;
            smoothedFPS = playbackFPS;

            readerInitialized = true;
            return readerInitialized;
        }


        public void UpdateFrame()
        {
            if (!readerInitialized)
                return;

            sequenceDeltaTime += Time.deltaTime * 1000;
            elapsedMsSinceSequenceStart += Time.deltaTime * 1000;
            if(elapsedMsSinceSequenceStart > targetFrameTimeMs * bufferedReader.totalFrames) //If we wrap around our ring buffer
            {
                elapsedMsSinceSequenceStart -= targetFrameTimeMs * bufferedReader.totalFrames;
                
                //For performance tracking
                lastSequenceCompletionTime = (Time.time - sequenceStartTime) * lastSequenceCompletionTime;
                sequenceStartTime = Time.time;
                framesDroppedCounter = 0;
            }

            int targetFrameIndex = Mathf.RoundToInt(elapsedMsSinceSequenceStart / targetFrameTimeMs);

            //Check how many frames our targetframe is in advance relative to the last played frame
            int framesInAdvance = 0;
            if(targetFrameIndex > lastFrameIndex)
                framesInAdvance = targetFrameIndex - lastFrameIndex;

            if(targetFrameIndex < lastFrameIndex)
                framesInAdvance = (bufferedReader.totalFrames - lastFrameIndex) + targetFrameIndex;
            
            frameDropped = framesInAdvance > 1 ? true : false;
            if(frameDropped)
                framesDroppedCounter += framesInAdvance - 1;

            //Debug.Log("Elapsed MS in sequence: " + elapsedMsSinceSequenceStart + ", target Index: " + targetFrameIndex + ", last frame: " + lastFrameIndex + ", target in advance: " + targetFrameIndex);

            bufferedReader.BufferFrames(targetFrameIndex, lastFrameIndex);          

            if (framesInAdvance > 0 )
            {              
                //Check if our desired frame is inside the frame buffer and loaded, so that we can use it
                int newBufferIndex = bufferedReader.GetBufferIndexForLoadedPlaybackIndex(targetFrameIndex);

                //Is the frame inside the buffer and fully loaded?
                if (newBufferIndex > -1)
                {
                    //Now that we a show a new frame, we mark the old played frame as consumed, and the new frame as playing
                    if(lastFrameBufferIndex > -1)
                        bufferedReader.frameBuffer[lastFrameBufferIndex].bufferState = BufferState.Consumed;
                    
                    bufferedReader.frameBuffer[newBufferIndex].bufferState = BufferState.Playing;
                    ShowFrameData(bufferedReader.frameBuffer[newBufferIndex], streamedMeshFilter, streamedMeshRenderer, pointcloudRenderer, bufferedReader.sequenceConfig, texture);
                    lastFrameBufferIndex = newBufferIndex;
                    lastFrameIndex = targetFrameIndex;

                    //Sometimes, the system might struggle to render a frame, or the application has a low framerate in general
                    //For performance tracking, we need to decouple the application framerate from our stream framerate. 
                    //If we are lagging behind due to these reasons, but have sucessfully catched up to the current target frame
                    //we still hit our target time window and the stream is performing well
                    //Therefore we substract the dropped frames from our deltatime

                    if (frameDropped && framesInAdvance > 1)
                        sequenceDeltaTime -= (framesInAdvance - 1) * targetFrameTimeMs; 

                    float decay = 0.9f;
                    smoothedFPS = decay * smoothedFPS + (1.0f - decay) * (1000f / sequenceDeltaTime);

                    lastFrameTime = sequenceDeltaTime;
                    sequenceDeltaTime = 0;
                }
            }

             if(frameDebugger != null)
             {
                frameDebugger.UpdateFrameDebugger(this);
             }

            //TODO: Buffering callback
        }

        public void SetFrameTime(float frameTimeMS)
        {
            elapsedMsSinceSequenceStart = frameTimeMS;
            lastFrameIndex = (int)(frameTimeMS / targetFrameTimeMs);
            UpdateFrame();
        }

        /// <summary>
        /// Display mesh and texture data from a frame buffer
        /// </summary>
        /// <param name="frame"></param>
        public void ShowFrameData(Frame frame, MeshFilter meshfilter, MeshRenderer meshRenderer, PointcloudRenderer pcRenderer, SequenceConfiguration config, Texture2D texture)
        {
            ShowGeometryData(frame, meshfilter, pcRenderer, config);       

            if (config.textureMode == SequenceConfiguration.TextureMode.PerFrame)
                ShowTextureData(frame, meshRenderer, texture);

            frame.finishedBufferingTime = 0;
        }


        /// <summary>
        /// Reads mesh or pointcloud data from a native array buffer
        /// </summary>
        /// <param name="frame"></param>
        void ShowGeometryData(Frame frame, MeshFilter meshFilter, PointcloudRenderer pcRenderer, SequenceConfiguration config)
        {
            frame.geoJobHandle.Complete();

            meshFilter.sharedMesh.bounds = config.GetBounds();

            if (config.geometryType == SequenceConfiguration.GeometryType.point)
            {
                pcRenderer.SetPointcloudData(frame.vertexBufferRaw, frame.sequenceConfiguration.verticeCounts[frame.playbackIndex]);
            }

            else
            {
                VertexAttributeDescriptor vp = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
                VertexAttributeDescriptor vt = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

                if (config.hasUVs)
                    meshFilter.sharedMesh.SetVertexBufferParams(config.maxVertexCount, vp, vt);
                else
                    meshFilter.sharedMesh.SetVertexBufferParams(config.maxVertexCount, vp);

                meshFilter.sharedMesh.SetIndexBufferParams(config.maxIndiceCount, IndexFormat.UInt32);
                meshFilter.sharedMesh.SetVertexBufferData<byte>(frame.vertexBufferRaw, 0, 0, frame.vertexBufferRaw.Length);
                meshFilter.sharedMesh.SetIndexBufferData<byte>(frame.indiceBufferRaw, 0, 0, frame.indiceBufferRaw.Length);
                meshFilter.sharedMesh.SetSubMesh(0, new SubMeshDescriptor(0, frame.sequenceConfiguration.indiceCounts[frame.playbackIndex]), MeshUpdateFlags.DontRecalculateBounds);
                meshFilter.sharedMesh.RecalculateNormals();
            }

        }

        /// <summary>
        /// Uploads texture data from a frame buffer to GPU
        /// </summary>
        /// <param name="frame"></param>
        void ShowTextureData(Frame frame, MeshRenderer renderer, Texture2D texture)
        {
            frame.textureJobHandle.Complete();
            texture.LoadRawTextureData<byte>(frame.textureBufferRaw);
            texture.Apply();
        }

        /// <summary>
        /// Create the object which will contains all MeshRenders, Filters and other data
        /// for this specific sequence
        /// </summary>
        /// <returns></returns>
        bool CreateStreamObject()
        {
            streamedMeshObject = new GameObject("StreamedMesh");
            streamedMeshObject.transform.parent = this.transform;

            streamedMeshObject.transform.localPosition = Vector3.zero;
            streamedMeshObject.transform.localRotation = Quaternion.identity;
            streamedMeshObject.transform.localScale = Vector3.one;

            return true;

        }

        bool ConfigureRenderObject(GameObject renderObject, SequenceConfiguration config, bool hidden, out MeshRenderer renderer, out MeshFilter meshfilter, out PointcloudRenderer pcRenderer, out Texture2D texture)
        {
            bool pc = config.geometryType == SequenceConfiguration.GeometryType.point;

            renderer = renderObject.GetComponent<MeshRenderer>();
            meshfilter = renderObject.GetComponent<MeshFilter>();
            pcRenderer = renderObject.GetComponent<PointcloudRenderer>();

            if (!meshfilter)
                meshfilter = renderObject.AddComponent<MeshFilter>();
            if (!meshfilter.sharedMesh)
                meshfilter.sharedMesh = new Mesh();
            if (!renderer)
                renderer = renderObject.AddComponent<MeshRenderer>();
            if (!pcRenderer && pc)
                pcRenderer = renderObject.AddComponent<PointcloudRenderer>();

            if(config.textureMode != SequenceConfiguration.TextureMode.None)
            {
                if(SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.DDS)
                    texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.DXT1, false);
                else if (SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.ASTC)
                    texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.ASTC_6x6, false);
                else
                {
                    texture = new Texture2D(1, 1);
                    Debug.LogError("Invalid texture format!");
                }
            }

            else
                {
                    texture = new Texture2D(1, 1);
                }
            
            

            if (hidden)
            {
                meshfilter.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                renderer.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                if (pc)
                    pcRenderer.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
            }

            SetupMaterials();

            if (!CheckMaterials())
                return false;
           
            if (pc)
                SetPointcloudMaterial(pointType, renderer);
            else
                renderer.sharedMaterial = new Material(meshMaterial);

            if (config.textureMode != SequenceConfiguration.TextureMode.None)
                ApplyTextureToMaterial(renderer.sharedMaterial, texture, materialSlots, customMaterialSlots);

            if (pc)
                pcRenderer.SetupPointcloudRenderer(config.maxVertexCount, meshfilter, pointSize);

            return true;
        }

        public bool SetupMaterials()
        {
            //Fill up material slots with default materials
            if (pointcloudMaterialQuads == null)
                //pointcloudMaterialQuads = Resources.Load("GS_Quads") as Material;
                pointcloudMaterialQuads = new Material(Resources.Load("GS_UnlitQuad") as Shader);

            if (pointcloudMaterialCircles == null)
                pointcloudMaterialCircles = Resources.Load("GS_Circles") as Material;

            if (pointcloudMaterialSplats == null)
                pointcloudMaterialSplats = Resources.Load("GS_SplatsExperimental") as Material;

            if (meshMaterial == null)
                meshMaterial = Resources.Load("GS_MeshMaterial_Unlit") as Material;

            return CheckMaterials();
        }

        public bool CheckMaterials()
        {
            if (meshMaterial == null)
            {
                UnityEngine.Debug.LogError("Mesh default material could not be loaded!");
                return false;
            }

            if (pointcloudMaterialQuads == null)
            {
                UnityEngine.Debug.LogError("Pointcloud Quads material could not be loaded!");
                return false;
            }

            if (pointcloudMaterialCircles == null)
            {
                UnityEngine.Debug.LogError("Pointcloud Circles material could not be loaded!");
                return false;
            }

            if (pointcloudMaterialSplats == null)
            {
                UnityEngine.Debug.LogError("Pointcloud Splats material could not be loaded!");
                return false;
            }


            return true;
        }

        public void ApplyTextureToMaterial(Material mat, Texture tex, MaterialProperties properties, List<string> customProperties)
        {
            if((MaterialProperties.Albedo & properties) == MaterialProperties.Albedo)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);

            }
                    

            if ((MaterialProperties.Emission & properties) == MaterialProperties.Emission)
            {
                if (mat.HasProperty("_EmissionMap"))
                    mat.SetTexture("_EmissionMap", tex);

            }                

            if ((MaterialProperties.Detail & properties) == MaterialProperties.Detail)
            {
                if (mat.HasProperty("_DetailAlbedoMap"))
                    mat.SetTexture("_DetailAlbedoMap", tex);
            }                

            foreach (string prop in customProperties)
            {
                if(mat.HasProperty(prop))
                    mat.SetTexture(prop, tex);

            }

            Vector2 scale = mat.GetTextureScale("_MainTex");
            mat.SetTextureScale("_MainTex", new Vector2(scale.x, scale.y * -1));
        }

        public void SetPointcloudMaterial(PointType type, MeshRenderer renderer)
        {
            pointType = type;

            switch (type)
            {
                case PointType.Quads:
                    renderer.sharedMaterial = pointcloudMaterialQuads;
                    break;
                case PointType.Circles:
                    renderer.sharedMaterial = pointcloudMaterialCircles;
                    break;
                case PointType.SplatsExperimental:
                    renderer.sharedMaterial = pointcloudMaterialSplats;
                    break;
                default:
                    break;
            }
        }

        public void SetPointSize(float pointSize)
        {
            if(pointcloudRenderer != null)
                pointcloudRenderer.SetPointSize(pointSize);
            if (thumbnailPCRenderer != null)
                thumbnailPCRenderer.SetPointSize(pointSize);
        }
        
        public MeshRenderer GetActiveRenderer()
        {
            if (streamedMeshRenderer != null)
                return streamedMeshRenderer;
            if (thumbnailMeshRenderer != null)
                return thumbnailMeshRenderer;

            else return null;
        }


        void CleanupSequence()
        {
            if (bufferedReader != null)
            {
                bufferedReader.DisposeFrameBuffer(true);
            }

            readerInitialized = false;

            CleanupMeshAndTexture();
        }

        void CleanupMeshAndTexture()
        {
            if (streamedMeshObject != null)
                Destroy(streamedMeshObject);

            if (texture != null)
                Destroy(texture);
        }



        [ExecuteInEditMode]
        void OnDestroy()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                ClearEditorThumbnail();
                return; 
            }
#endif
            CleanupSequence();
        }

        private void Reset()
        {
            SetupMaterials();
        }

        #region Thumbnail

#if UNITY_EDITOR

        /// <summary>
        /// Loads and shows a thumbnail of the clip that was just opened. Only shown in the editor
        /// </summary>
        /// <param name="pathToSequence"></param>
        public void LoadEditorThumbnail(string pathToSequence)
        {
            ClearEditorThumbnail();

            if(GetComponent<GeometrySequencePlayer>() != null)
                if (GetComponent<GeometrySequencePlayer>().GetRelativeSequencePath().Length == 0)
                    return;

            if (Directory.Exists(pathToSequence))
            {
                thumbnailReader = new BufferedGeometryReader(gameObject, null);
                if (!thumbnailReader.SetupReader(pathToSequence, 1))
                {
                    Debug.LogWarning("Could not load thumbnail for sequence: " + pathToSequence);
                    return;
                }

                ConfigureRenderObject(gameObject, thumbnailReader.sequenceConfig, true, out thumbnailMeshRenderer, out thumbnailMeshFilter, out thumbnailPCRenderer, out thumbnailTexture);

                Frame thumbnail = thumbnailReader.frameBuffer[0];
                thumbnailReader.SetupFrameForReading(thumbnail, thumbnailReader.sequenceConfig, 0);
                thumbnailReader.ScheduleGeometryReadJob(thumbnail, thumbnailReader.plyFilePaths[0]);

                if (thumbnailReader.sequenceConfig.geometryType == SequenceConfiguration.GeometryType.point)
                    thumbnailPCRenderer.SetPointSize(pointSize);

                if (thumbnailReader.sequenceConfig.textureMode != SequenceConfiguration.TextureMode.None)
                {
                    thumbnailReader.ScheduleTextureReadJob(thumbnail, thumbnailReader.GetDeviceDependendentTexturePath(0));
                    ShowTextureData(thumbnail, thumbnailMeshRenderer, thumbnailTexture);
                }

                ShowFrameData(thumbnail, thumbnailMeshFilter, thumbnailMeshRenderer, thumbnailPCRenderer, thumbnailReader.sequenceConfig, thumbnailTexture);
            }
        }

        /// <summary>
        /// Removes the shown Thumbnail
        /// </summary>
        public void ClearEditorThumbnail()
        {
            if (thumbnailReader != null)
            {
                thumbnailReader.DisposeFrameBuffer(false);
                thumbnailReader = null;
            }

            if (thumbnailPCRenderer != null)
                thumbnailPCRenderer.EndEditorLife();

            if (thumbnailTexture != null)
                DestroyImmediate(thumbnailTexture);
        }
#endif
        #endregion
        
        #region Debug
        void AttachFrameDebugger()
        {
            #if UNITY_EDITOR
            GameObject debugGO = Resources.Load("GSFrameDebugger") as GameObject;
            frameDebugger = Instantiate(debugGO).GetComponent<GSFrameDebugger>();
            frameDebugger.GetCanvas().renderMode = RenderMode.ScreenSpaceOverlay;
            #endif
        }
        #endregion
    }
}
