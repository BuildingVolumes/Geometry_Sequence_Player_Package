using BuildingVolumes.Player;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEditor.Experimental.GraphView.GraphView;

namespace BuildingVolumes.Player
{

  public class PointcloudRendererRT : MonoBehaviour, IPointCloudRenderer
  {
    ComputeShader computeShaderRT;
    RenderTexture rtPositions;
    RenderTexture rtColors;
    int rtResolution;

    GraphicsBuffer pointSourceBuffer;

    GameObject pcObject;
    MeshFilter pcMeshFilter;
    MeshRenderer pcMeshRenderer;

    Material currentPointcloudMaterial;
    float currentPointSize = 0;
    float currentPointEmission = 1;

    bool ready = true;

    //Compute shader property IDs
    static readonly int pointSourceBufferID = Shader.PropertyToID("_PointSourceBuffer");
    static readonly int pointCountID = Shader.PropertyToID("_PointCount");
    static readonly int rtPositionsID = Shader.PropertyToID("_RTPositions");
    static readonly int rtColorsID = Shader.PropertyToID("_RTColors");
    static readonly int rtStrideID = Shader.PropertyToID("_RTStride");

    //Vertex/Fragment shader property IDs
    static readonly int rtResolutionID = Shader.PropertyToID("_RTResolution");
    static readonly int rtPositionSourceID = Shader.PropertyToID("_PositionSourceRT");
    static readonly int rtColorSourceID = Shader.PropertyToID("_ColorSourceRT");
    static readonly int pointScaleID = Shader.PropertyToID("_PointScale");
    static readonly int pointEmissionID = Shader.PropertyToID("_PointEmission");

    /// <summary>
    /// Prepare all the buffers for a pointcloud sequence. Only needs to bet set once per sequence
    /// </summary>
    /// <param name="maxPointCount">The maximum number of points that could appear in any frame of the sequence</param>
    /// <param name="meshFilter">The meshfilter where the point geometry data will be rendered into</param>
    /// <param name="meshRenderer">The meshrenderer used for rendering the points. Will be auto-configured</param>
    public void Setup(SequenceConfiguration configuration, Transform parent, float pointSize, float pointEmission, Material pointMaterial, bool instantiateMaterial)
    {
      Dispose();

      ready = true;

      if (computeShaderRT == null)
        computeShaderRT = Resources.Load("ShaderGraph/Pointcloud_Shadergraph", typeof(ComputeShader)) as ComputeShader;

      //Calculate a square shaped texture in which all point data will fit
      rtResolution = Mathf.CeilToInt(Mathf.Sqrt(configuration.maxVertexCount));

      //Render Texture for storing point positions
      rtPositions = new RenderTexture(rtResolution, rtResolution, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
      rtPositions.enableRandomWrite = true;
      rtPositions.filterMode = FilterMode.Point;
      rtPositions.Create();

      //Render texture for storing colors
      rtColors = new RenderTexture(rtResolution, rtResolution, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
      rtColors.enableRandomWrite = true;
      rtColors.filterMode = FilterMode.Point;
      rtColors.Create();

      //Create the buffer where all the raw point data will be stored
      int textureSize = rtPositions.width * rtPositions.height;
      pointSourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, textureSize, 4 * 4);

      computeShaderRT.SetInt(rtStrideID, rtResolution);
      computeShaderRT.SetBuffer(0, pointSourceBufferID, pointSourceBuffer);
      computeShaderRT.SetTexture(0, rtPositionsID, rtPositions);
      computeShaderRT.SetTexture(0, rtColorsID, rtColors);

      //Create the pointcloud mesh with n points
      pcObject = MeshCreation(configuration);

      SetPointcloudMaterial(pointMaterial, pointSize, pointEmission, instantiateMaterial);
    }

    /// <summary>
    /// On Polyspatial, mesh creation is a relatively expensive process
    /// We therefore need to slowly create the mesh over multiple frames
    /// and also distribute the mesh over multiple Meshfilters.
    /// Otherwise, we risk fatal crashes where the AVP needs to restart
    /// </summary>
    GameObject MeshCreation(SequenceConfiguration config)
    {
      //Setup the rendering object
      GameObject pcObject = CreateStreamObject("PointcloudRenderer", this.transform);

      pcMeshFilter = pcObject.GetComponent<MeshFilter>();
      if (pcMeshFilter == null)
        pcMeshFilter = pcObject.AddComponent<MeshFilter>();

      pcMeshRenderer = pcObject.GetComponent<MeshRenderer>();
      if (pcMeshRenderer == null)
        pcMeshRenderer = pcObject.AddComponent<MeshRenderer>();

      pcMeshFilter.hideFlags = HideFlags.HideAndDontSave;
      pcMeshRenderer.hideFlags = HideFlags.HideAndDontSave;


      //Create a mesh that we use to render the pointcloud
      //The mesh consists out of simple quads, which we define here

      int quadCount = config.maxVertexCount;

      Vector3 vertice1 = new Vector3(-0.5f, 0.5f, 0f);
      Vector3 vertice2 = new Vector3(0.5f, 0.5f, 0f);
      Vector3 vertice3 = new Vector3(0.5f, -0.5f, 0f);
      Vector3 vertice4 = new Vector3(-0.5f, -0.5f, 0f);

      Vector2 uv1 = new Vector2(0, 1);
      Vector2 uv2 = new Vector2(1, 1);
      Vector2 uv3 = new Vector2(1, 0);
      Vector2 uv4 = new Vector2(0, 0);

      Mesh mesh = new Mesh();

      Vector3[] vertices = new Vector3[quadCount * 4];
      Vector2[] uvs = new Vector2[quadCount * 4];
      int[] indices = new int[quadCount * 6];

      for (int i = 0; i < quadCount; i++)
      {
        vertices[i * 4 + 0] = vertice1;
        vertices[i * 4 + 1] = vertice2;
        vertices[i * 4 + 2] = vertice3;
        vertices[i * 4 + 3] = vertice4;

        uvs[i * 4 + 0] = uv1;
        uvs[i * 4 + 1] = uv2;
        uvs[i * 4 + 2] = uv3;
        uvs[i * 4 + 3] = uv4;

        indices[i * 6 + 0] = i * 4 + 0;
        indices[i * 6 + 1] = i * 4 + 1;
        indices[i * 6 + 2] = i * 4 + 3;
        indices[i * 6 + 3] = i * 4 + 1;
        indices[i * 6 + 4] = i * 4 + 2;
        indices[i * 6 + 5] = i * 4 + 3;
      }

      //Important, as we often deal with more than 16000 triangles
      mesh.indexFormat = IndexFormat.UInt32;

      mesh.vertices = vertices;
      mesh.triangles = indices;
      mesh.SetUVs(0, uvs);
      mesh.bounds = config.GetBounds();
      pcMeshFilter.sharedMesh = mesh;

      return pcObject;
    }


    /// <summary>
    /// Update the pointcloud data in the sequence with a new pointcloud frame.
    /// </summary>
    /// <param name="pointSource">A native buffer of points with their colors and positions</param>
    /// <param name="pointCount">The number of points in the current frame</param>
    public void SetFrame(Frame frame)
    {
      if (!ready)
        return;

      frame.geoJobHandle.Complete();
      NativeArray<byte> pointdataGPU = pointSourceBuffer.LockBufferForWrite<byte>(0, frame.geoJob.vertexBuffer.Length); //Locking buffer is faster than GraphicsBuffer.SetData;
      frame.geoJob.vertexBuffer.CopyTo(pointdataGPU);
      pointSourceBuffer.UnlockBufferAfterWrite<byte>(frame.geoJob.vertexBuffer.Length);
      computeShaderRT.SetInt(pointCountID, frame.geoJob.vertexCount);
      int groupSize = Mathf.CeilToInt(rtPositions.width / 32f);
      computeShaderRT.Dispatch(0, groupSize, groupSize, 1);
    }


    public void SetPointcloudMaterial(Material mat, bool instantiateMaterial)
    {
      SetPointcloudMaterial(mat, currentPointSize, currentPointEmission, instantiateMaterial);
    }

    public void SetPointcloudMaterial(Material mat, float pointSize, float pointEmission, bool instantiateMaterial)
    {
      if (!mat)
        mat = LoadDefaultMaterial();

      currentPointcloudMaterial = mat;
      currentPointSize = pointSize;
      currentPointEmission = pointEmission;

      Material newMat;
      
      if(instantiateMaterial)
        newMat = new Material(mat);
      else
        newMat = mat;
      newMat.SetFloat(rtResolutionID, rtResolution);
      newMat.SetTexture(rtPositionSourceID, rtPositions);
      newMat.SetTexture(rtColorSourceID, rtColors);

      if (newMat.HasFloat(pointScaleID))
        newMat.SetFloat(pointScaleID, pointSize);

      if (newMat.HasFloat(pointEmissionID))
        newMat.SetFloat(pointEmissionID, pointEmission);

      if (pcMeshRenderer != null)
        pcMeshRenderer.sharedMaterial = newMat;
    }

    public void Show()
    {
      if (pcMeshRenderer)
        pcMeshRenderer.enabled = true;
    }

    public void Hide()
    {
      if (pcMeshRenderer)
        pcMeshRenderer.enabled = true;
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

    public Material LoadDefaultMaterial()
    {
      Material pointcloudDefaultMaterial = new Material(Resources.Load("ShaderGraph/Pointcloud_Circles_Shadergraph", typeof(Material)) as Material);

      if (pointcloudDefaultMaterial == null)
        UnityEngine.Debug.LogError("Pointcloud Quads material (Polyspatial) could not be loaded!");

      return pointcloudDefaultMaterial;
    }

    public void SetPointSize(float size)
    {
      currentPointSize = size;

      if (pcMeshRenderer)
        if (pcMeshRenderer.sharedMaterial.HasFloat(pointScaleID))
          pcMeshRenderer.sharedMaterial.SetFloat(pointScaleID, size);
    }

    public void SetPointEmission(float emission)
    {
      currentPointEmission = emission;

      if (pcMeshRenderer)
        if (pcMeshRenderer.sharedMaterial.HasFloat(pointEmissionID))
          pcMeshRenderer.sharedMaterial.SetFloat(pointEmissionID, emission);
    }

    public void Dispose()
    {
      if (rtPositions != null)
        DestroyImmediate(rtPositions);
      if (rtColors != null)
        DestroyImmediate(rtColors);
      if (pointSourceBuffer != null)
        pointSourceBuffer.Dispose();
      if (pcObject != null)
        DestroyImmediate(pcObject);
    }
  }

}