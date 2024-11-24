using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;


namespace BuildingVolumes.Streaming
{    public class PointcloudRenderer : MonoBehaviour
    {
        [HideInInspector] public ComputeShader computeShader;
        [HideInInspector] public float pointScale = 0.005f;

        GraphicsBuffer pointSourceBuffer;
        GraphicsBuffer vertexBuffer;
        GraphicsBuffer indexBuffer;

        GraphicsBuffer rotateToCameraMat;

        int sourcePointCount;
        bool isDataSet;

        MeshFilter outputMeshFilter;
        Mesh outputMesh;
        Camera cam;

        public float angle;

        static readonly int vertexBufferID = Shader.PropertyToID("_VertexBuffer");
        static readonly int pointSourceID = Shader.PropertyToID("_PointSourceBuffer");
        static readonly int indexBufferID = Shader.PropertyToID("_IndexBuffer");
        static readonly int rotateToCameraID = Shader.PropertyToID("_RotateToCamera");
        static readonly int pointScaleID = Shader.PropertyToID("_PointScale");
        static readonly int pointCountID = Shader.PropertyToID("_PointCount");


        private void Update()
        {
            Render();
        }

        /// <summary>
        /// Set the pointcloud data to be rendered. Does only need to be set once per Pointcloud
        /// </summary>
        /// <param name="pointSource">The point positions and colors as a native array</param>
        /// <param name="sourceCount">The amount of points contained in the array</param>
        public void SetPointcloudData(NativeArray<byte> pointSource, int sourceCount)
        {
            if (!enabled)
                return;

            sourcePointCount = sourceCount;
            NativeArray<byte> pointdataGPU = pointSourceBuffer.LockBufferForWrite<byte>(0, pointSourceBuffer.count * 4 * 4); //Locking buffer is faster than GraphicsBuffer.SetData;
            pointSource.CopyTo(pointdataGPU);
            pointSourceBuffer.UnlockBufferAfterWrite<byte>(pointSource.Length);
            computeShader.SetInt(pointCountID, sourcePointCount);
            isDataSet = true;
        }

        /// <summary>
        /// Renders the currently set pointcloud to the screen. This process is sensitive to the camera used for rendering,
        /// as the points always need to be oriented towards the camera.
        /// </summary>
        public void Render()
        {
            if (!isDataSet || !enabled)
                return;

#if UNITY_EDITOR
            cam = Camera.current;
#endif
            if (cam == null)
                cam = Camera.main;

            if(cam == null)
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
        
        /// <summary>
        /// Prepares all buffers used for pointcloud rendering. This needs to be executed once per sequence
        /// All buffers will be allocated with the max. possible size that can appear in the sequence to avoid
        /// re-allocations
        /// </summary>
        /// <param name="maxPointCount">The max. numbers of point that will be shown in any frame of the sequence</param>
        /// <param name="renderToMeshFilter">The mesh filter which will contains the final mesh of the pointclouds</param>
        /// <param name="pointSize">The diameter of the points in Unity units</param>
        /// <returns></returns>
        public int SetupPointcloudRenderer(int maxPointCount, MeshFilter renderToMeshFilter, float pointSize)
        {
            ReleaseLargeBuffers();
            ReleaseSmallBuffers();

            rotateToCameraMat = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
            pointSourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite,  maxPointCount, 4 * 4);

            if (computeShader == null)
                computeShader = Resources.Load("PointcloudCompute") as ComputeShader;

            outputMeshFilter = renderToMeshFilter;

            if (outputMeshFilter.sharedMesh == null)
                outputMeshFilter.sharedMesh = new Mesh();

            outputMesh = outputMeshFilter.sharedMesh;

            outputMesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            outputMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

            VertexAttributeDescriptor vp = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            VertexAttributeDescriptor vc = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);
            VertexAttributeDescriptor vt = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

            outputMesh.SetVertexBufferParams(maxPointCount * 4, vp, vc, vt);
            outputMesh.SetIndexBufferParams(maxPointCount * 6, IndexFormat.UInt32);

            outputMesh.SetSubMesh(0, new SubMeshDescriptor(0, maxPointCount * 6), MeshUpdateFlags.DontRecalculateBounds);

            vertexBuffer = outputMesh.GetVertexBuffer(0);
            indexBuffer = outputMesh.GetIndexBuffer();

            computeShader.SetBuffer(0, vertexBufferID, vertexBuffer);
            computeShader.SetBuffer(0, indexBufferID, indexBuffer);
            computeShader.SetBuffer(0, rotateToCameraID, rotateToCameraMat);
            computeShader.SetBuffer(0, pointSourceID, pointSourceBuffer);
            SetPointSize(pointSize);


#if UNITY_EDITOR
            if (!Application.isPlaying)
                StartEditorLife();
#endif
            return maxPointCount;
        }

        private void OnDisable()
        {
            ReleaseSmallBuffers();
            ReleaseLargeBuffers();
        }


        void ReleaseLargeBuffers()
        {
            if (vertexBuffer != null)
                vertexBuffer.Release();
            if(pointSourceBuffer != null)            
                pointSourceBuffer.Release();
            if(indexBuffer != null)
                indexBuffer.Release();
            
        }

        void ReleaseSmallBuffers()
        {
            if(rotateToCameraMat != null)
                rotateToCameraMat.Release();
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
            ReleaseLargeBuffers();
            ReleaseSmallBuffers();
        }

        [ExecuteInEditMode]

        private void OnDestroy()
        {
            EndEditorLife();
        }
#endif

        #endregion
    }
}


