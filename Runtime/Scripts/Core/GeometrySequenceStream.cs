using UnityEngine;
using System.IO;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using System.Runtime.InteropServices.ComTypes;
using UnityEditor.SceneManagement;

namespace BuildingVolumes.Streaming
{
    public class GeometrySequenceStream : MonoBehaviour
    {
        public string pathToSequence { get; private set; }

        public Transform parentTransform;

        public int bufferSize = 30;
        public bool useAllThreads = true;
        public int threadCount = 4;

        public Material pointcloudMaterial;
        public Material meshMaterial;

        bool readerIsReady = false;

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

            if (GetComponent<MeshRenderer>())
                GetComponent<MeshRenderer>().enabled = false;
        }

        /// <summary>
        /// Loads and shows a thumbnail of the clip that was just opened. Only shown in the editor
        /// </summary>
        /// <param name="pathToSequence"></param>
        public void LoadEditorThumbnail(string pathToSequence)
        {
            Frame thumbnail = new Frame();

            if (Directory.Exists(pathToSequence))
            {
                BufferedGeometryReader reader = new BufferedGeometryReader(pathToSequence, 1);

                if (reader.plyFilePaths.Length > 0 && reader.texturesFilePath == null)
                    thumbnail = reader.LoadSingleFrame(reader.plyFilePaths[0], null);

                else if (reader.plyFilePaths.Length > 0 && reader.texturesFilePath.Length > 0)
                    thumbnail = reader.LoadSingleFrame(reader.plyFilePaths[0], reader.texturesFilePath[0]);

                MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
                MeshFilter meshFilter = GetComponent<MeshFilter>();

                if (!meshFilter)
                    meshFilter = gameObject.AddComponent<MeshFilter>();
                if (!meshRenderer)
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();

                thumbnail.textureMode = TextureMode.None;

                if (thumbnail.plyHeaderInfo.meshType == MeshTopology.Points)
                    meshRenderer.material = pointcloudMaterial;

                else
                {
                    meshRenderer.material = meshMaterial;
                    if (thumbnail.textureBufferRaw.Length > 0)
                        thumbnail.textureMode = TextureMode.PerFrame;
                }

                ShowFrameData(thumbnail, meshRenderer, meshFilter);

                if (thumbnail.textureBufferRaw.Length > 0)
                    thumbnail.textureBufferRaw.Dispose();

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            }
        }

        /// <summary>
        /// Removes the shown Thumbnail
        /// </summary>
        public void ClearEditorThumbnail()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            MeshFilter meshFilter = GetComponent<MeshFilter>();

            if (meshFilter)
                DestroyImmediate(meshFilter);
            if (!meshRenderer)
                DestroyImmediate(meshRenderer);
        }

        /// <summary>
        /// Cleans up the current sequence and prepares the playback of the sequence in the given folder. Doesn't start playback!
        /// </summary>
        /// <param name="absolutePathToSequence">The absolute path to the folder containing a sequence of .ply geometry files and optionally .dds texture files</param>
        public bool ChangeSequence(string absolutePathToSequence, float playbackFPS)
        {
            CleanupSequence();
            CleanupMeshAndTexture();
            currentFrameIndex = 0;

            this.pathToSequence = absolutePathToSequence;

            bufferedReader = new BufferedGeometryReader(pathToSequence, bufferSize);

            bool meshRes = SetupMesh();
            bool textureRes = SetupTexture();
            readerIsReady = meshRes && textureRes;

            if (!readerIsReady)
            {
                UnityEngine.Debug.LogError("Reader could not be set up correctly, stopping playback!");
                return false;
            }

            targetFrameTimeMs = 1000f / (float)playbackFPS;

            return true;
        }

        public void UpdateFrame(float playbackTimeInMs)
        {
            if (!readerIsReady)
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
                    ShowFrameData(bufferedReader.frameBuffer[frameBufferIndex], streamedMeshRenderer, streamedMeshFilter);
                    bufferedReader.frameBuffer[frameBufferIndex].isDisposed = true;

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

        bool SetupMesh()
        {
            streamedMeshObject = new GameObject("StreamedMesh");

            if (parentTransform != null)
                streamedMeshObject.transform.parent = parentTransform;
            else
                streamedMeshObject.transform.parent = this.transform;

            streamedMeshObject.transform.localPosition = Vector3.zero;
            streamedMeshObject.transform.localRotation = Quaternion.identity;
            streamedMeshObject.transform.localScale = Vector3.one;

            string[] paths;

            try { paths = Directory.GetFiles(pathToSequence, "*.ply"); }

            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("Error getting sequence files, folder is probably empty: " + pathToSequence);
                return false;
            }

            if (paths.Length == 0)
            {
                UnityEngine.Debug.LogError("Couldn't find .ply files in sequence directory: " + pathToSequence);
                return false;
            }

            if (pointcloudMaterial == null || meshMaterial == null)
            {

            }

            BinaryReader headerReader = new BinaryReader(new FileStream(paths[0], FileMode.Open));

            string line = "";
            bool mesh = false;

            while (!line.Contains("end_header"))
            {
                line = bufferedReader.ReadPLYHeaderLine(headerReader);

                if (line.Contains("face"))
                    mesh = true;
            }

            headerReader.Dispose();

            streamedMeshFilter = streamedMeshObject.AddComponent<MeshFilter>();
            streamedMeshFilter.mesh = new Mesh();
            streamedMeshRenderer = streamedMeshObject.AddComponent<MeshRenderer>();

            if (mesh)
            {
                if (meshMaterial == null)
                {
                    UnityEngine.Debug.LogError("Mesh material not assigned in GeometrySequenceStream Component, please assign a material!");
                    return false;
                }

                streamedMeshRenderer.material = new Material(meshMaterial);
                streamedMeshRenderer.material.SetTexture("_MainTex", texture);
            }

            else
            {
                if (meshMaterial == null)
                {
                    UnityEngine.Debug.LogError("Pointcloud material not assigned in GeometrySequenceStream Component, please assign a material!");
                    return false;
                }

                streamedMeshRenderer.material = new Material(pointcloudMaterial);
            }

            return true;
        }

        bool SetupTexture()
        {
            string[] textureFiles = Directory.GetFiles(pathToSequence + "/", "*.dds");

            HeaderDDS headerDDS = new HeaderDDS();

            TextureMode textureMode = TextureMode.None;

            if (textureFiles.Length > 0)
            {
                headerDDS = bufferedReader.ReadDDSHeader(textureFiles[0]);

                if (headerDDS.error)
                    return false;

                texture = new Texture2D(headerDDS.width, headerDDS.height, TextureFormat.DXT1, false);

                //Case: A single texture for the whole geometry sequence
                if (textureFiles.Length == 1)
                {
                    textureMode = TextureMode.Single;

                    //In this case we simply pre-load the texture at the start
                    Frame textureLoad = new Frame();
                    textureLoad.textureBufferRaw = new NativeArray<byte>(headerDDS.size, Allocator.Persistent);
                    textureLoad = bufferedReader.ScheduleTextureJob(textureLoad, textureFiles[0]);
                    ShowTextureData(textureLoad, streamedMeshRenderer);
                    textureLoad.textureBufferRaw.Dispose();
                }

                //Case: Each frame has its own texture
                if (textureFiles.Length > 1)
                    textureMode = TextureMode.PerFrame;

            }

            else
                textureMode = TextureMode.None;

            if (!bufferedReader.SetupTextureReader(textureMode, headerDDS))
                return false;

            return true;
        }



        /// <summary>
        /// Display mesh and texture data from a frame buffer
        /// </summary>
        /// <param name="frame"></param>
        public void ShowFrameData(Frame frame, MeshRenderer meshrenderer, MeshFilter meshFilter)
        {
            ShowGeometryData(frame, meshFilter);

            if (frame.ddsHeaderInfo.size > 0)
            {
                ShowTextureData(frame, meshrenderer);
            }
        }


        /// <summary>
        /// Reads mesh data from a native array buffer and disposes of it right after 
        /// </summary>
        /// <param name="frame"></param>
        void ShowGeometryData(Frame frame, MeshFilter meshFilter)
        {
            if (frame.plyHeaderInfo.error)
                return;

            frame.geoJobHandle.Complete();

            if (meshFilter.sharedMesh == null)
                meshFilter.sharedMesh = new Mesh();

            Mesh.ApplyAndDisposeWritableMeshData(frame.meshArray, meshFilter.sharedMesh);
            meshFilter.sharedMesh.RecalculateBounds();

            if (frame.plyHeaderInfo.meshType == MeshTopology.Triangles)
                meshFilter.sharedMesh.RecalculateNormals();
        }

        /// <summary>
        /// Reads texture data from a frame buffer. Doesn't dispose of the data, you need to do that manually!
        /// </summary>
        /// <param name="frame"></param>
        void ShowTextureData(Frame frame, MeshRenderer meshRenderer)
        {
            if (frame.ddsHeaderInfo.error)
                return;

            frame.textureJobHandle.Complete();

            NativeArray<byte> textureRaw = texture.GetRawTextureData<byte>();
            HeaderDDS textureHeader = frame.ddsHeaderInfo;

            if (textureRaw.Length != frame.textureBufferRaw.Length)
            {
                texture = new Texture2D(textureHeader.width, textureHeader.height, TextureFormat.DXT1, false);
                textureRaw = texture.GetRawTextureData<byte>();
            }

            textureRaw.CopyFrom(frame.textureBufferRaw);
            texture.Apply();

            if (meshRenderer.material.GetTexture("_MainTex") != texture)
                meshRenderer.material.SetTexture("_MainTex", texture);
        }

        public void SetupMaterials()
        {
#if UNITY_EDITOR
            //Fill up material slots with default materials
            if (pointcloudMaterial == null)
            {
                string[] Guids = AssetDatabase.FindAssets("GS_PointcloudMaterial t:material");
                if (Guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(Guids[0]);
                    Material mat = (Material)AssetDatabase.LoadAssetAtPath(path, typeof(Material));
                    pointcloudMaterial = mat;
                }
            }

            if (meshMaterial == null)
            {
                string[] Guids = AssetDatabase.FindAssets("GS_MeshMaterial t:material");
                if (Guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(Guids[0]);
                    Material mat = (Material)AssetDatabase.LoadAssetAtPath(path, typeof(Material));
                    meshMaterial = mat;
                }
            }
#endif
        }


        void CleanupSequence()
        {
            if (bufferedReader != null)
            {
                bufferedReader.DisposeAllFrames(true, true, true);
            }

        }

        void CleanupMeshAndTexture()
        {
            if (streamedMeshObject != null)
                Destroy(streamedMeshObject);

            if (texture != null)
                Destroy(texture);
        }

        public void DisposeDisplayedGeometry()
        {
            streamedMeshFilter.sharedMesh = null;
            streamedMeshFilter.mesh = new Mesh();
        }

        void OnDestroy()
        {
            CleanupSequence();
        }

        private void Reset()
        {
            if (pointcloudMaterial == null && meshMaterial == null)
                SetupMaterials();
        }

    }

}
