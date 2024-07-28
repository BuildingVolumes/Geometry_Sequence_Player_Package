using UnityEngine;
using System.IO;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Rendering;


#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BuildingVolumes.Streaming
{
    public class GeometrySequenceStream : MonoBehaviour
    {
        public string pathToSequence { get; private set; }

        public Transform parentTransform;
        public Bounds drawBounds = new Bounds(Vector3.zero, new Vector3(3, 3, 3));

        public int bufferSize = 30;
        public bool useAllThreads = true;
        public int threadCount = 4;

        public Material pointcloudMaterial;
        public Material meshMaterial;
        public float pointSize;

        public PointcloudRenderer pointcloudRenderer;

        MeshFilter thumbnailMeshFilter;
        MeshRenderer thumbnailMeshRenderer;
        PointcloudRenderer thumbnailPCRenderer;
        BufferedGeometryReader thumbnailReader;
        Texture2D thumbnailTexture;

        bool readerInitialized = false;

        public bool frameDropped = false;
        public int currentFrameIndex = 0;
        public float targetFrameTimeMs = 0;
        public float elapsedMsSinceLastFrame = 0;
        public float smoothedFPS = 0f;

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

        private void Awake()
        {
            if (!useAllThreads)
                JobsUtility.JobWorkerCount = threadCount;
            else
                JobsUtility.JobWorkerCount = JobsUtility.JobWorkerMaximumCount;

            if (GetComponent<MeshRenderer>())
                GetComponent<MeshRenderer>().enabled = false;

#if !UNITY_EDITOR && !UNITY_STANDALONE_WIN && !UNITY_STANDALONE_OSX && !UNITY_STANDALONE_LINUX && !UNITY_IOS && !UNITY_VISIONOS && !UNITY_ANDROID && !UNITY_TVOS
            Debug.LogError("Platform not supported by Geometry Sequence Streamer! Playback will probably fail");
#endif
        }

        /// <summary>
        /// Cleans up the current sequence and prepares the playback of the sequence in the given folder. Doesn't start playback!
        /// </summary>
        /// <param name="absolutePathToSequence">The absolute path to the folder containing a sequence of .ply geometry files and optionally .dds texture files</param>
        public bool ChangeSequence(string absolutePathToSequence, float playbackFPS)
        {
            CleanupSequence();
            readerInitialized = false;
            currentFrameIndex = 0;

            pathToSequence = absolutePathToSequence;

            bufferedReader = new BufferedGeometryReader();
            if (!bufferedReader.SetupReader(pathToSequence, bufferSize))
                return readerInitialized;

            if (!CreateStreamObject())
                return readerInitialized;

            //If we have a single texture in the sequence, we read it immidiatly
            if (bufferedReader.sequenceConfig.textureMode == SequenceConfiguration.TextureMode.Single)
            {
                    bufferedReader.SetupFrameForReading(bufferedReader.frameBuffer[0], bufferedReader.sequenceConfig, 0);
                    bufferedReader.ScheduleTextureReadJob(bufferedReader.frameBuffer[0], bufferedReader.GetDeviceDependendentTexturePath(0));
                    ShowTextureData(bufferedReader.frameBuffer[0], texture);
            }

            targetFrameTimeMs = 1000f / (float)playbackFPS;

            readerInitialized = true;
            return readerInitialized;
        }

        public void UpdateFrame(float playbackTimeInMs)
        {
            if (!readerInitialized)
                return;

            elapsedMsSinceLastFrame += Time.deltaTime * 1000;

            int targetFrameIndex = Mathf.RoundToInt(playbackTimeInMs / targetFrameTimeMs);

            //Fill the buffer with new data from the disk, and delete unused frames (In case of lag/skip)
            bufferedReader.BufferFrames(targetFrameIndex);

            if (targetFrameIndex != currentFrameIndex && targetFrameIndex < bufferedReader.totalFrames)
            {
                //Check if our desired frame is inside the frame buffer and loaded, so that we can use it
                int frameBufferIndex = bufferedReader.GetBufferIndexForLoadedPlaybackIndex(targetFrameIndex);

                //Is the frame inside the buffer and fully loaded?
                if (frameBufferIndex > -1)
                {
                    //The frame has been loaded and we'll show the model (& texture)
                    ShowFrameData(bufferedReader.frameBuffer[frameBufferIndex], streamedMeshFilter, streamedMeshObject, pointcloudRenderer, bufferedReader.sequenceConfig, texture);
                    bufferedReader.frameBuffer[frameBufferIndex].wasConsumed = true;

                    float decay = 0.95f;
                    if (elapsedMsSinceLastFrame > 0)
                        smoothedFPS = decay * smoothedFPS + (1.0f - decay) * (1000f / elapsedMsSinceLastFrame);

                    elapsedMsSinceLastFrame = 0;
                }

                if (Mathf.Abs(targetFrameIndex - currentFrameIndex) > 1 && targetFrameIndex > 0)
                    frameDropped = true;

                currentFrameIndex = targetFrameIndex;
            }

            //TODO: Buffering callback

        }

        /// <summary>
        /// Display mesh and texture data from a frame buffer
        /// </summary>
        /// <param name="frame"></param>
        public void ShowFrameData(Frame frame, MeshFilter meshFilter, GameObject streamObject, PointcloudRenderer pcRenderer, SequenceConfiguration config, Texture2D texture)
        {
            ShowGeometryData(frame, meshFilter, streamObject, pcRenderer, config);

            if (config.textureMode == SequenceConfiguration.TextureMode.PerFrame)
                ShowTextureData(frame, texture);
        }


        /// <summary>
        /// Reads mesh data from a native array buffer
        /// </summary>
        /// <param name="frame"></param>
        void ShowGeometryData(Frame frame, MeshFilter meshFilter, GameObject streamObject, PointcloudRenderer pcRenderer, SequenceConfiguration config)
        {
            frame.geoJobHandle.Complete();

            meshFilter.sharedMesh.bounds = config.GetBounds();

            if (config.geometryType == SequenceConfiguration.GeometryType.point)
            {
                pcRenderer.SetPointcloudData(frame.vertexBufferRaw, frame.sequenceConfiguration.verticeCounts[frame.playbackIndex], meshFilter.transform);
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
                meshFilter.sharedMesh.SetIndexBufferData<int>(frame.indiceBufferRaw, 0, 0, frame.sequenceConfiguration.indiceCounts[frame.playbackIndex]);
                meshFilter.sharedMesh.SetSubMesh(0, new SubMeshDescriptor(0, frame.sequenceConfiguration.indiceCounts[frame.playbackIndex]), MeshUpdateFlags.DontRecalculateBounds);
                meshFilter.sharedMesh.RecalculateNormals();
            }
        }

        /// <summary>
        /// Reads texture data from a frame buffer. Doesn't dispose of the data, you need to do that manually!
        /// </summary>
        /// <param name="frame"></param>
        void ShowTextureData(Frame frame, Texture2D texture)
        {
            frame.textureJobHandle.Complete();
            texture.SetPixelData<byte>(frame.textureBufferRaw, 0);
            texture.Apply();
        }

        bool CreateStreamObject()
        {
            streamedMeshObject = new GameObject("StreamedMesh");

            if (parentTransform != null)
                streamedMeshObject.transform.parent = parentTransform;
            else
                streamedMeshObject.transform.parent = this.transform;

            streamedMeshObject.transform.localPosition = Vector3.zero;
            streamedMeshObject.transform.localRotation = Quaternion.identity;
            streamedMeshObject.transform.localScale = Vector3.one;

            return ConfigureRenderObject(streamedMeshObject, bufferedReader.sequenceConfig, false, out streamedMeshRenderer, out streamedMeshFilter, out pointcloudRenderer, out texture);
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

            if(SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.DDS)
                texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.DXT1, false);
            else if (SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.ASTC)
                texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.ASTC_6x6, false);
            else
            {
                texture = new Texture2D(1, 1);
                Debug.LogError("Unsupported Texture Format!");
            }

            if (hidden)
            {
                meshfilter.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                renderer.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                if (pc)
                    pcRenderer.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
            }

            if (!CheckMaterials())
                return false;

            if (pc)
                renderer.sharedMaterial = pointcloudMaterial; //new Material?
            else
                renderer.sharedMaterial = meshMaterial;

            if (config.textureMode != SequenceConfiguration.TextureMode.None)
                renderer.sharedMaterial.SetTexture("_MainTex", texture);

            if (pc)
                pcRenderer.SetupPointcloudRenderer(config.maxVertexCount, meshfilter);

            return true;
        }

        public bool SetupMaterials()
        {
            //Fill up material slots with default materials
            if (pointcloudMaterial == null)
                pointcloudMaterial = Resources.Load("GS_VertexColorMat") as Material;

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

            if (pointcloudMaterial == null)
            {
                UnityEngine.Debug.LogError("Pointcloud default material could not be loaded!");
                return false;
            }

            return true;
        }

        public void SetPointSize(float size)
        {
            pointSize = size;

            if (pointcloudRenderer != null)
                pointcloudRenderer.SetPointSize(size);
            if (thumbnailPCRenderer != null)
                thumbnailPCRenderer.SetPointSize(size);
        }

       


        void CleanupSequence()
        {
            if (bufferedReader != null)
            {
                bufferedReader.DisposeFrameBuffer(true);
            }

            CleanupMeshAndTexture();
        }

        void CleanupMeshAndTexture()
        {
            if (streamedMeshObject != null)
                Destroy(streamedMeshObject);

            if (texture != null)
                Destroy(texture);
        }

        void OnDestroy()
        {
            CleanupSequence();
            Debug.Log("Destroyed!");
        }

        private void Reset()
        {
            if (pointcloudMaterial == null && meshMaterial == null)
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

            if (Directory.Exists(pathToSequence))
            {
                thumbnailReader = new BufferedGeometryReader();
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
                    ShowTextureData(thumbnail, thumbnailTexture);
                }


                ShowFrameData(thumbnail, thumbnailMeshFilter, gameObject, thumbnailPCRenderer, thumbnailReader.sequenceConfig, thumbnailTexture);
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

    }
}
