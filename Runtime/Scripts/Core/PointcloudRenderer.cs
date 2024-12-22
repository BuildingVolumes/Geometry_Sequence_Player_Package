using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
namespace BuildingVolumes.Streaming
{
    public class PointcloudRenderer : MonoBehaviour, IPointCloudRenderer
    {
        private ComputeShader computeShader;
        private float pointScale = 0.005f;

        GraphicsBuffer pointSourceBuffer;
        GraphicsBuffer vertexBuffer;
        GraphicsBuffer indexBuffer;
        GraphicsBuffer rotateToCameraMat;

        GameObject pcObject;
        MeshFilter pcMeshFilter;
        MeshRenderer pcMeshRenderer;
        Mesh pcMesh;

        Material pcMaterialQuads;
        Material pcMaterialCircles;
        Material pcMaterialSplats;

        Camera cam;

        int sourcePointCount;
        bool isDataSet;

        static readonly int vertexBufferID = Shader.PropertyToID("_VertexBuffer");
        static readonly int pointSourceID = Shader.PropertyToID("_PointSourceBuffer");
        static readonly int indexBufferID = Shader.PropertyToID("_IndexBuffer");
        static readonly int rotateToCameraID = Shader.PropertyToID("_RotateToCamera");
        static readonly int pointScaleID = Shader.PropertyToID("_PointScale");
        static readonly int pointCountID = Shader.PropertyToID("_PointCount");

        /// <summary>
        /// Prepares all buffers used for pointcloud rendering. This needs to be executed once per sequence
        /// All buffers will be allocated with the max. possible size that can appear in the sequence to avoid
        /// re-allocations
        /// </summary>
        /// <param name="maxPointCount">The max. numbers of point that will be shown in any frame of the sequence</param>
        /// <param name="meshfilter">The mesh filter which will contains the final mesh of the pointclouds</param>
        /// <param name="pointSize">The diameter of the points in Unity units</param>
        /// <returns></returns>
        public void Setup(SequenceConfiguration configuration, Transform parent, float pointSize, GeometrySequenceStream.PointType pointType)
        {
            Dispose();

            pcObject = CreateStreamObject("PointcloudRenderer", parent);

            pcMeshFilter = pcObject.GetComponent<MeshFilter>();
            if (pcMeshFilter == null)
                pcMeshFilter = pcObject.AddComponent<MeshFilter>();

            pcMeshRenderer = pcObject.GetComponent<MeshRenderer>();
            if (pcMeshRenderer == null)
                pcMeshRenderer = pcObject.AddComponent<MeshRenderer>();

            if (pcMeshFilter.sharedMesh == null)
                pcMeshFilter.sharedMesh = new Mesh();

            pcMeshFilter.hideFlags = HideFlags.HideAndDontSave;
            pcMeshRenderer.hideFlags = HideFlags.HideAndDontSave;

            pcMesh = pcMeshFilter.sharedMesh;
            pcMesh.bounds = configuration.GetBounds();

            if (computeShader == null)
                computeShader = Resources.Load("PointcloudCompute") as ComputeShader;

            rotateToCameraMat = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
            pointSourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, configuration.maxVertexCount, 4 * 4);

            pcMesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            pcMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

            VertexAttributeDescriptor vp = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            VertexAttributeDescriptor vc = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);
            VertexAttributeDescriptor vt = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

            pcMesh.SetVertexBufferParams(configuration.maxVertexCount * 4, vp, vc, vt);
            pcMesh.SetIndexBufferParams(configuration.maxVertexCount * 6, IndexFormat.UInt32);
            pcMesh.SetSubMesh(0, new SubMeshDescriptor(0, configuration.maxVertexCount * 6), MeshUpdateFlags.DontRecalculateBounds);

            vertexBuffer = pcMesh.GetVertexBuffer(0);
            indexBuffer = pcMesh.GetIndexBuffer();

            computeShader.SetBuffer(0, vertexBufferID, vertexBuffer);
            computeShader.SetBuffer(0, indexBufferID, indexBuffer);
            computeShader.SetBuffer(0, rotateToCameraID, rotateToCameraMat);
            computeShader.SetBuffer(0, pointSourceID, pointSourceBuffer);

            LoadMaterials();
            SetPointcloudMaterial(pointType);
            SetPointSize(pointSize);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                StartEditorLife();
#endif
        }

        /// <summary>
        /// Set the pointcloud data to be rendered. Does only need to be set once per Pointcloud
        /// </summary>
        /// <param name="pointSource">The point positions and colors as a native array</param>
        /// <param name="sourceCount">The amount of points contained in the array</param>
        public void RenderFrame(Frame frame)
        {
            if (!enabled)
                return;
            frame.geoJobHandle.Complete();
            sourcePointCount = frame.geoJob.vertexCount;
            NativeArray<byte> pointdataGPU = pointSourceBuffer.LockBufferForWrite<byte>(0, pointSourceBuffer.count * 4 * 4); //Locking buffer is faster than GraphicsBuffer.SetData;
            frame.geoJob.vertexBuffer.CopyTo(pointdataGPU);
            pointSourceBuffer.UnlockBufferAfterWrite<byte>(frame.geoJob.vertexBuffer.Length);
            computeShader.SetInt(pointCountID, sourcePointCount);
            isDataSet = true;
        }

        private void Update()
        {
            Render();
        }

        /// <summary>
        /// Renders the currently set pointcloud to the screen. This process is sensitive to the camera used for rendering,
        /// as the points always need to be oriented towards the camera.
        /// </summary>
        void Render()
        {
            if (!isDataSet || !enabled)
                return;

#if UNITY_EDITOR
            cam = Camera.current;
#endif
            if (cam == null)
                cam = Camera.main;

            if (cam == null)
            {
                Debug.LogError("Could not find main camera. Please tag one camera as Main Camera for the Pointcloud renderer to work");
                return;
            }

            //Rotation that lets the points face the camera
            Quaternion fromObjectToCamera = Quaternion.Inverse(transform.rotation) * (Quaternion.LookRotation(cam.transform.forward, cam.transform.up));
            Matrix4x4 rotateToCamMat = Matrix4x4.Rotate(fromObjectToCamera);
            rotateToCameraMat.SetData(new Matrix4x4[] { rotateToCamMat });

            int groupSize = Mathf.CeilToInt(sourcePointCount / 128f);
            computeShader.Dispatch(0, groupSize, 1, 1);

        }

        /// <summary>
        /// Set the point diameter in Unity units. 
        /// </summary>
        /// <param name="size"></param>
        public void SetPointSize(float size)
        {
            pointScale = size / 2; //Divide by two, otherwise the diameter will be twice as large as expected
            computeShader.SetFloat(pointScaleID, pointScale);

#if UNITY_EDITOR 
            if (!Application.isPlaying)
            {
                Render();
                SceneView.RepaintAll();
            }
#endif
        }

        GameObject CreateStreamObject(string name, Transform parent)
        {
            GameObject newStreamObject = new GameObject(name);
            newStreamObject.transform.parent = this.transform;
            newStreamObject.transform.localPosition = Vector3.zero;
            newStreamObject.transform.localRotation = Quaternion.identity;
            newStreamObject.transform.localScale = Vector3.one;
            newStreamObject.hideFlags = HideFlags.DontSave;
            return newStreamObject;
        }

        public void SetPointcloudMaterial(GeometrySequenceStream.PointType type)
        {
            pcMeshRenderer.sharedMaterial = GetPointcloudMaterialFromType(type);
        }

        public Material GetPointcloudMaterialFromType(GeometrySequenceStream.PointType type)
        {
            switch (type)
            {
                case GeometrySequenceStream.PointType.Quads:
                    return pcMaterialQuads;
                case GeometrySequenceStream.PointType.Circles:
                    return pcMaterialCircles;
                case GeometrySequenceStream.PointType.SplatsExperimental:
                    return pcMaterialSplats;
                default:
                    return null;
            }
        }

        public void Show()
        {
            pcMeshRenderer.enabled = true;
        }

        public void Hide()
        {
            pcMeshRenderer.enabled = false;
        }

        public void LoadMaterials()
        {
            //Fill up material slots with default materials
            if (pcMaterialQuads == null)
                pcMaterialQuads = Resources.Load("GS_Quads") as Material;

            if (pcMaterialCircles == null)
                pcMaterialCircles = Resources.Load("GS_Circles") as Material;

            if (pcMaterialSplats == null)
                pcMaterialSplats = Resources.Load("GS_SplatsExperimental") as Material;
        }

        [ExecuteInEditMode]
        private void OnDestroy()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (vertexBuffer != null)
                vertexBuffer.Release();
            if (pointSourceBuffer != null)
                pointSourceBuffer.Release();
            if (indexBuffer != null)
                indexBuffer.Release();
            if (rotateToCameraMat != null)
                rotateToCameraMat.Release();

            if(pcMeshFilter != null)
                if(pcMeshFilter.sharedMesh == null)
                    DestroyImmediate(pcMeshFilter.sharedMesh);
            if(pcObject != null)
                DestroyImmediate(pcObject);
        }

        #region RenderInEditor

#if UNITY_EDITOR
        public void StartEditorLife()
        {
            SceneView.beforeSceneGui += RenderInEditor;
        }

        public void RenderInEditor(SceneView view)
        {
            if (this == null)
                EndEditorLife();
            else
                Render();
        }

        public void EndEditorLife()
        {
            SceneView.beforeSceneGui -= RenderInEditor;
            Dispose();
        }
#endif

        #endregion
    }
}


