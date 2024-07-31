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

        public void SetPointcloudData(NativeArray<byte> pointSource, int sourceCount)
        {
            if (!enabled)
                return;

            sourcePointCount = sourceCount;
            NativeArray<byte> pointdataGPU = pointSourceBuffer.LockBufferForWrite<byte>(0, pointSourceBuffer.count); //Locking buffer is faster than GraphicsBuffer.SetData;
            pointSource.CopyTo(pointdataGPU);
            pointSourceBuffer.UnlockBufferAfterWrite<byte>(pointSource.Length);
            computeShader.SetInt(pointCountID, sourcePointCount);
            isDataSet = true;           
        }

        public void Render()
        {
            if (!isDataSet || !enabled)
                return;

#if UNITY_EDITOR
            cam = Camera.current;
#endif
            if (cam == null)
                cam = Camera.main;

            //Rotation that lets the points face the camera
            Quaternion fromObjectToCamera = Quaternion.Inverse(transform.rotation) * (Quaternion.LookRotation(cam.transform.forward, cam.transform.up));
            Matrix4x4 rotateToCamMat = Matrix4x4.Rotate(fromObjectToCamera);
            rotateToCameraMat.SetData(new Matrix4x4[] { rotateToCamMat });

            int groupSize = Mathf.CeilToInt(sourcePointCount / 128f);
            computeShader.Dispatch(0, groupSize, 1, 1);
        }

        public void SetPointSize(float size)
        {
            pointScale = size;
            computeShader.SetFloat(pointScaleID, pointScale);

#if UNITY_EDITOR 
            if (!Application.isPlaying)
            {
                Render();
                SceneView.RepaintAll();
            }
#endif
        }
         
        public int SetupPointcloudRenderer(int maxPointCount, MeshFilter renderToMeshFilter)
        {
            ReleaseLargeBuffers();
            ReleaseSmallBuffers();

            rotateToCameraMat = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
            pointSourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite,  maxPointCount * 4 * 4, 4 * 4);

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

            outputMesh.SetVertexBufferParams(maxPointCount * 4, vp, vc);
            outputMesh.SetIndexBufferParams(maxPointCount * 6, IndexFormat.UInt32);

            outputMesh.SetSubMesh(0, new SubMeshDescriptor(0, maxPointCount * 6), MeshUpdateFlags.DontRecalculateBounds);

            vertexBuffer = outputMesh.GetVertexBuffer(0);
            indexBuffer = outputMesh.GetIndexBuffer();

            computeShader.SetBuffer(0, vertexBufferID, vertexBuffer);
            computeShader.SetBuffer(0, indexBufferID, indexBuffer);
            computeShader.SetBuffer(0, rotateToCameraID, rotateToCameraMat);
            computeShader.SetBuffer(0, pointSourceID, pointSourceBuffer);


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


