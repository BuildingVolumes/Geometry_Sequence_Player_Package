using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
namespace BuildingVolumes.Player
{
  public class PointcloudRenderer : MonoBehaviour, IPointCloudRenderer
  {
    private ComputeShader computeShader;
    private float pointScale = 0.005f;
    private float pointEmission = 1f;

    GraphicsBuffer pointSourceBuffer1;
    int pointSourceCount1 = 0;
    GraphicsBuffer pointSourceBuffer2;
    int pointSourceCount2 = 0;
    GraphicsBuffer pointSourceBuffer3;
    int pointSourceCount3 = 0;

    GraphicsBuffer vertexBuffer;
    GraphicsBuffer indexBuffer;
    GraphicsBuffer rotateToCameraMat;

    GameObject pcObject;
    MeshFilter pcMeshFilter;
    MeshRenderer pcMeshRenderer;
    Mesh pcMesh;

    Material pointcloudMaterial;

    Camera cam;

    int maxPointCount;
    bool isDataSet;
    bool buffersInitialized;
    int bufferUpdateIndex;
    int lastBufferUpdateFrame = -1;

    static readonly int vertexBufferID = Shader.PropertyToID("_VertexBuffer");
    static readonly int pointSourceID1 = Shader.PropertyToID("_PointSourceBuffer1");
    static readonly int pointSourceID2 = Shader.PropertyToID("_PointSourceBuffer2");
    static readonly int pointSourceID3 = Shader.PropertyToID("_PointSourceBuffer3");
    static readonly int indexBufferID = Shader.PropertyToID("_IndexBuffer");
    static readonly int rotateToCameraID = Shader.PropertyToID("_RotateToCamera");
    static readonly int pointScaleID = Shader.PropertyToID("_PointScale");
    static readonly int pointCountID1 = Shader.PropertyToID("_PointCount1");
    static readonly int pointCountID2 = Shader.PropertyToID("_PointCount2");
    static readonly int pointCountID3 = Shader.PropertyToID("_PointCount3");

    /// <summary>
    /// Prepares all buffers used for pointcloud rendering. This needs to be executed once per sequence
    /// All buffers will be allocated with the max. possible size that can appear in the sequence to avoid
    /// re-allocations
    /// </summary>
    /// <param name="maxPointCount">The max. numbers of point that will be shown in any frame of the sequence</param>
    /// <param name="meshfilter">The mesh filter which will contains the final mesh of the pointclouds</param>
    /// <param name="pointSize">The diameter of the points in Unity units</param>
    /// <returns></returns>
    public void Setup(SequenceConfiguration configuration, Transform parent, float pointSize, float emission, Material mat, bool instantiateMaterial)
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
        computeShader = (ComputeShader)Instantiate(Resources.Load("Legacy/Pointcloud", typeof(ComputeShader)));

      rotateToCameraMat = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4 * 4 * 4);
      pointSourceBuffer1 = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, configuration.maxVertexCount, 4 * 4);
      pointSourceBuffer2 = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, configuration.maxVertexCount, 4 * 4);
      pointSourceBuffer3 = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, configuration.maxVertexCount, 4 * 4);

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
      computeShader.SetBuffer(1, vertexBufferID, vertexBuffer);
      computeShader.SetBuffer(2, vertexBufferID, vertexBuffer);
      computeShader.SetBuffer(0, indexBufferID, indexBuffer);
      computeShader.SetBuffer(1, indexBufferID, indexBuffer);
      computeShader.SetBuffer(2, indexBufferID, indexBuffer);
      computeShader.SetBuffer(0, rotateToCameraID, rotateToCameraMat);
      computeShader.SetBuffer(1, rotateToCameraID, rotateToCameraMat);
      computeShader.SetBuffer(2, rotateToCameraID, rotateToCameraMat);
      computeShader.SetBuffer(0, pointSourceID1, pointSourceBuffer1);
      computeShader.SetBuffer(1, pointSourceID2, pointSourceBuffer2);
      computeShader.SetBuffer(2, pointSourceID3, pointSourceBuffer3);

      //Run a shader dispatch that creates only invisible quads to clean the buffers
      int groupSize = Mathf.CeilToInt(configuration.maxVertexCount / 128f);
      computeShader.SetInt(pointCountID1, 0);
      computeShader.Dispatch(2, groupSize, 1, 1);

      pointcloudMaterial = mat;
      if (!pointcloudMaterial)
        pointcloudMaterial = LoadDefaultMaterial();
      SetPointcloudMaterial(mat, pointSize, emission, instantiateMaterial);

      buffersInitialized = true;

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
    public void SetFrame(Frame frame)
    {
      if (!buffersInitialized)
        return;
      if (lastBufferUpdateFrame == Time.frameCount)
        return;

      frame.geoJobHandle.Complete();

      bufferUpdateIndex++;
      if (bufferUpdateIndex == 3)
        bufferUpdateIndex = 0;

      if (bufferUpdateIndex == 0)
      {
        pointSourceCount1 = frame.geoJob.vertexCount;
        UpdatePointBuffer(frame, pointSourceBuffer1);
      }

      if (bufferUpdateIndex == 1)
      {
        pointSourceCount2 = frame.geoJob.vertexCount;
        UpdatePointBuffer(frame, pointSourceBuffer2);
      }

      if (bufferUpdateIndex == 2)
      {
        pointSourceCount3 = frame.geoJob.vertexCount;
        UpdatePointBuffer(frame, pointSourceBuffer3);
      }

      lastBufferUpdateFrame = Time.frameCount;
    }

    void UpdatePointBuffer(Frame frame, GraphicsBuffer pointBuffer)
    {
      maxPointCount = pointBuffer.count;
      NativeArray<byte> pointdataGPU = pointBuffer.LockBufferForWrite<byte>(0, pointBuffer.count * 4 * 4); //Locking buffer is faster than GraphicsBuffer.SetData;
      frame.geoJob.vertexBuffer.CopyTo(pointdataGPU);
      pointBuffer.UnlockBufferAfterWrite<byte>(frame.geoJob.vertexBuffer.Length);
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
      if (!isDataSet || !buffersInitialized)
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
      int groupSize = Mathf.CeilToInt(maxPointCount / 128f);


      if (bufferUpdateIndex == 0)
      {
        computeShader.SetInt(pointCountID1, pointSourceCount1);
        computeShader.Dispatch(0, groupSize, 1, 1);
      }

      if (bufferUpdateIndex == 1)
      {
        computeShader.SetInt(pointCountID2, pointSourceCount2);
        computeShader.Dispatch(1, groupSize, 1, 1);
      }

      if (bufferUpdateIndex == 2)
      {
        computeShader.SetInt(pointCountID3, pointSourceCount3);
        computeShader.Dispatch(2, groupSize, 1, 1);
      }
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

    public void SetPointEmission(float emission)
    {
      if (pcMeshRenderer)
        if (pcMeshRenderer.sharedMaterial.HasProperty("_Emission"))
          pcMeshRenderer.sharedMaterial.SetFloat("_Emission", emission);
      pointEmission = emission;
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

    public void SetPointcloudMaterial(Material mat, bool instantiateMaterial)
    {
      if (pcMeshRenderer)
      {
        if (mat)
          if (instantiateMaterial)
            pcMeshRenderer.material = new Material(mat);
          else
            pcMeshRenderer.material = mat;
        else
            pcMeshRenderer.material = LoadDefaultMaterial();
      }
    }

    public void SetPointcloudMaterial(Material mat, float pointSize, float pointEmission, bool instantiateMaterial)
    {
      SetPointcloudMaterial(mat, instantiateMaterial);
      SetPointSize(pointSize);
      SetPointEmission(pointEmission);
    }

    public void Show()
    {
      pcMeshRenderer.enabled = true;
    }

    public void Hide()
    {
      pcMeshRenderer.enabled = false;
    }

    public Material LoadDefaultMaterial()
    {
      Material mat = Resources.Load("Legacy/Pointcloud_Circles_Legacy", typeof(Material)) as Material;

      if (!mat)
        Debug.LogError("Could not load default pointcloud material at Resources/Legacy/Pointcloud_Circles_Legacy");

      return mat;

    }

    public void Dispose()
    {
      if (vertexBuffer != null)
        vertexBuffer.Release();
      if (pointSourceBuffer1 != null)
        pointSourceBuffer1.Release();
      if (pointSourceBuffer2 != null)
        pointSourceBuffer2.Release();
      if (pointSourceBuffer3 != null)
        pointSourceBuffer3.Release();
      if (indexBuffer != null)
        indexBuffer.Release();
      if (rotateToCameraMat != null)
        rotateToCameraMat.Release();

      if (pcMeshFilter != null)
        if (pcMeshFilter.sharedMesh == null)
          DestroyImmediate(pcMeshFilter.sharedMesh);
      if (pcObject != null)
        DestroyImmediate(pcObject);

      buffersInitialized = false;
      isDataSet = false;

      if (computeShader != null)
        DestroyImmediate(computeShader);
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


