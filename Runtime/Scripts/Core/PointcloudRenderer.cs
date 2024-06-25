using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;


namespace BuildingVolumes.Streaming
{    public class PointcloudRenderer : MonoBehaviour
    {
        public ComputeShader computeShader;        

        public float pointScale = 0.005f;

        GraphicsBuffer pointSourceBuffer;
        GraphicsBuffer vertexBuffer;
        GraphicsBuffer indexBuffer;

        GraphicsBuffer toSourceWorldBuffer;
        GraphicsBuffer cameraToWorldBuffer;
        GraphicsBuffer worldToCameraBuffer;

        MeshFilter outputMeshFilter;
        Mesh outputMesh;
        Camera cam;


        static readonly int vertexBufferID = Shader.PropertyToID("_VertexBuffer");
        static readonly int pointSourceID = Shader.PropertyToID("_PointSourceBuffer");
        static readonly int indexBufferID = Shader.PropertyToID("_IndexBuffer");
        static readonly int matrixToSourceWorldID = Shader.PropertyToID("_toSourceWorld");
        static readonly int matrixCameraToWorldID = Shader.PropertyToID("_CameraToWorld");
        static readonly int matrixWorldToCameraID = Shader.PropertyToID("_WorldToCamera");
        static readonly int pointScaleID = Shader.PropertyToID("_PointScale");
        static readonly int pointCountID = Shader.PropertyToID("_PointCount");

        private void Start()
        {
            toSourceWorldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
            cameraToWorldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
            worldToCameraBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
        }

        // Update is called once per frame
        public void Render(NativeArray<byte> pointSourceArray, int sourcePointCount, Transform pointSourceTransform)
        {
            if (!enabled)
                return;

            // Get the vertex buffer of the source point mesh, and set it up
            // as a buffer parameter to a compute shader. This will act as a
            //position and color source for the rendered points
            pointSourceBuffer.SetData<byte>(pointSourceArray);

            //This point renderer will not always be at the same position as the Point Source object.
            //To handle this, we first convert the coordinates of this point renderer back into local space.
            //From the local space, we then convert them back into the world space of the point source, so that
            //the rendered points are always spatially locked to the point source object.
            Matrix4x4 rendererWorldToLocal = transform.worldToLocalMatrix;
            Matrix4x4 sourceLocalToWorld = pointSourceTransform.localToWorldMatrix;
            Matrix4x4 toSourceWorld = rendererWorldToLocal * sourceLocalToWorld;
            toSourceWorldBuffer.SetData(new Matrix4x4[] { toSourceWorld });

//Use the Unity Editor Viewport camera if we are in the editor
#if UNITY_EDITOR
            if(Camera.current != null)
                cam = Camera.current;
#endif

            //We also need to rotate the vertices, so that they always face the camera.
            //For this we get the rotation matrix, that rotates from the source point to the camera
            cameraToWorldBuffer.SetData(new Matrix4x4[] { cam.cameraToWorldMatrix });
            worldToCameraBuffer.SetData(new Matrix4x4[] { cam.worldToCameraMatrix });

            computeShader.SetBuffer(0, pointSourceID, pointSourceBuffer);
            computeShader.SetBuffer(0, vertexBufferID, vertexBuffer);
            computeShader.SetBuffer(0, indexBufferID, indexBuffer);
            computeShader.SetBuffer(0, matrixToSourceWorldID, toSourceWorldBuffer);
            computeShader.SetBuffer(0, matrixWorldToCameraID, worldToCameraBuffer);
            computeShader.SetBuffer(0, matrixCameraToWorldID, cameraToWorldBuffer);
            computeShader.SetFloat(pointScaleID, pointScale);
            computeShader.SetInt(pointCountID, sourcePointCount);
            int groupSize = Mathf.CeilToInt(sourcePointCount / 128f);
            computeShader.Dispatch(0, groupSize, 1, 1);
        }



        public int SetupPointcloudRenderer(int maxPointCount, MeshFilter renderToMeshFilter)
        {
            ReleaseLargeBuffers();

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
            outputMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10);

            vertexBuffer = outputMesh.GetVertexBuffer(0);
            indexBuffer = outputMesh.GetIndexBuffer();

            pointSourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, maxPointCount * 4 * 4, 4 * 4);

            cam = Camera.main;

            return maxPointCount;
        }

        private void OnDisable()
        {
            ReleaseSmallBuffers();
            ReleaseLargeBuffers();
            Object.Destroy(outputMesh);
        }

        void ReleaseLargeBuffers()
        {
            if (vertexBuffer != null)
            {
                //Don't know which function should be used, so just do both
                pointSourceBuffer.Release();
                vertexBuffer.Release();
                indexBuffer.Release();
            }
        }

        void ReleaseSmallBuffers()
        {
            toSourceWorldBuffer.Release();
            cameraToWorldBuffer.Release();
            worldToCameraBuffer.Release();
        }
    }
}


