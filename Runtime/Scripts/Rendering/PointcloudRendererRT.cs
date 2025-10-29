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
    RenderTexture rtNormals;
    RenderTexture rtColors;
    int rtResolution;

    //Triple buffering neccessary, as not all dispatches are guranteed
    //to perform in one frame
    GraphicsBuffer[] pointSourceBuffers = new GraphicsBuffer[3];
    int bufferIndex = 0;

    GameObject pcObject;
    MeshFilter pcMeshFilter;
    MeshRenderer pcMeshRenderer;

    Material currentPointcloudMaterial;
    float currentPointSize = 0;
    float currentPointEmission = 1;

    bool ready = true;
    bool isDisposed;

    //Compute shader property IDs
    static readonly int pointSourceBufferID = Shader.PropertyToID("_PointSourceBuffer");
    static readonly int pointSourceStrideID = Shader.PropertyToID("_SourceStride");
    static readonly int pointCountID = Shader.PropertyToID("_PointCount");
    static readonly int rtPositionsID = Shader.PropertyToID("_RTPositions");
    static readonly int rtColorsID = Shader.PropertyToID("_RTColors");
    static readonly int rtNormalsID = Shader.PropertyToID("_RTNormals");
    static readonly int rtStrideID = Shader.PropertyToID("_RTStride");
    static readonly int rtNormalsEnabledID = Shader.PropertyToID("_RTHasNormals");

    //Vertex/Fragment shader property IDs
    static readonly int rtResolutionID = Shader.PropertyToID("_RTResolution");
    static readonly int rtPositionSourceID = Shader.PropertyToID("_PositionSourceRT");
    static readonly int rtNormalSourceID = Shader.PropertyToID("_NormalSourceRT");
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
      isDisposed = false;

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

      //Optional Render Texture for storing normals
      if (configuration.hasNormals)
      {
        rtNormals = new RenderTexture(rtResolution, rtResolution, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
        rtNormals.enableRandomWrite = true;
        rtNormals.filterMode = FilterMode.Point;
        rtNormals.Create();
      }

      //Create the buffer where all the raw point data will be stored
      int textureSize = rtPositions.width * rtPositions.height;
      int stride = configuration.hasNormals ? 4 * 7 : 4 * 4;

      for (int i = 0; i < pointSourceBuffers.Length; i++)
      {
        pointSourceBuffers[i] = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, textureSize, stride);
      }

      int kernel = configuration.hasNormals ? 1 : 0;
      computeShaderRT.SetInt(rtStrideID, rtResolution);
      computeShaderRT.SetInt(pointSourceStrideID, stride);
      computeShaderRT.SetBool(rtNormalsEnabledID, configuration.hasNormals);
      computeShaderRT.SetTexture(kernel, rtPositionsID, rtPositions);
      computeShaderRT.SetTexture(kernel, rtColorsID, rtColors);
      if (configuration.hasNormals)
        computeShaderRT.SetTexture(kernel, rtNormalsID, rtNormals);

      //Create the pointcloud mesh with n points
      pcObject = MeshCreation(configuration);

      SetPointcloudMaterial(pointMaterial, pointSize, pointEmission, instantiateMaterial, configuration.hasNormals);

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

      Vector3 normal = new Vector3(0, 0, 1);

      Vector2 uv1 = new Vector2(0, 1);
      Vector2 uv2 = new Vector2(1, 1);
      Vector2 uv3 = new Vector2(1, 0);
      Vector2 uv4 = new Vector2(0, 0);

      Mesh mesh = new Mesh();

      Vector3[] vertices = new Vector3[quadCount * 4];
      Vector3[] normals = new Vector3[1];
      Vector2[] uvs = new Vector2[quadCount * 4];
      int[] indices = new int[quadCount * 6];
      if (config.hasNormals)
        normals = new Vector3[quadCount * 4];

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

        if (config.hasNormals)
        {
          normals[i * 4 + 0] = normal;
          normals[i * 4 + 1] = normal;
          normals[i * 4 + 2] = normal;
          normals[i * 4 + 3] = normal;
        }

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
      if (config.hasNormals)
        mesh.normals = normals;

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
      if (!ready || isDisposed)
        return;

      bufferIndex++;
      if (bufferIndex >= 3)
        bufferIndex = 0;

      frame.geoJobHandle.Complete();
      NativeArray<byte> pointdataGPU = pointSourceBuffers[bufferIndex].LockBufferForWrite<byte>(0, frame.geoJob.vertexBuffer.Length); //Locking buffer is faster than GraphicsBuffer.SetData;
      frame.geoJob.vertexBuffer.CopyTo(pointdataGPU);
      pointSourceBuffers[bufferIndex].UnlockBufferAfterWrite<byte>(frame.geoJob.vertexBuffer.Length);
      computeShaderRT.SetInt(pointCountID, frame.geoJob.vertexCount);

      int groupSize = Mathf.CeilToInt(rtPositions.width / 32f);
      int kernel = frame.sequenceConfiguration.hasNormals ? 1 : 0;
      computeShaderRT.SetBuffer(kernel, pointSourceBufferID, pointSourceBuffers[bufferIndex]);
      computeShaderRT.Dispatch(kernel, groupSize, groupSize, 1);
    }


    public void SetPointcloudMaterial(Material mat, bool instantiateMaterial)
    {
      SetPointcloudMaterial(mat, currentPointSize, currentPointEmission, instantiateMaterial);
    }

    public void SetPointcloudMaterial(Material mat, float pointSize, float pointEmission, bool instantiateMaterial)
    {
      SetPointcloudMaterial(mat, pointSize, pointEmission, instantiateMaterial, false);
    }

    public void SetPointcloudMaterial(Material mat, float pointSize, float pointEmission, bool instantiateMaterial, bool hasNormals = false)
    {
      if (isDisposed || !pcMeshRenderer)
        return;

      if (!mat)
        mat = LoadDefaultMaterial(hasNormals);

      currentPointcloudMaterial = mat;
      currentPointSize = pointSize;
      currentPointEmission = pointEmission;

      Material newMat;

      if (instantiateMaterial)
        newMat = new Material(mat);
      else
        newMat = mat;
      newMat.SetFloat(rtResolutionID, rtResolution);
      newMat.SetTexture(rtPositionSourceID, rtPositions);
      newMat.SetTexture(rtColorSourceID, rtColors);
      if (hasNormals)
        newMat.SetTexture(rtNormalSourceID, rtNormals);

      if (newMat.HasFloat(pointScaleID))
        newMat.SetFloat(pointScaleID, pointSize);

      if (newMat.HasFloat(pointEmissionID))
        newMat.SetFloat(pointEmissionID, pointEmission);

      if (pcMeshRenderer != null)
        pcMeshRenderer.sharedMaterial = newMat;
    }

    public void Show()
    {
      if (isDisposed || !pcMeshRenderer)
        return;

      pcMeshRenderer.enabled = true;
    }

    public void Hide()
    {
      if (isDisposed || !pcMeshRenderer)
        return;

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

    public Material LoadDefaultMaterial(bool hasNormals)
    {
      Material mat;

      if (hasNormals)
        mat = new Material(Resources.Load("ShaderGraph/Pointcloud_Circles_Lit_Shadergraph", typeof(Material)) as Material);
      else
        mat = new Material(Resources.Load("ShaderGraph/Pointcloud_Circles_Shadergraph", typeof(Material)) as Material);

      if (mat == null)
        UnityEngine.Debug.LogError("Pointcloud Quads material (Polyspatial) could not be loaded!");

      return mat;
    }

    public void SetPointSize(float size)
    {
      if (isDisposed || !pcMeshRenderer)
        return;

      if (pcMeshRenderer.sharedMaterial.HasFloat(pointScaleID))
        pcMeshRenderer.sharedMaterial.SetFloat(pointScaleID, size);

      currentPointSize = size;
    }

    public void SetPointEmission(float emission)
    {
      if (isDisposed || !pcMeshRenderer)
        return;

      if (pcMeshRenderer.sharedMaterial.HasFloat(pointEmissionID))
        pcMeshRenderer.sharedMaterial.SetFloat(pointEmissionID, emission);

      currentPointEmission = emission;
    }

    public void Dispose()
    {
      if (rtPositions != null)
        DestroyImmediate(rtPositions);
      if (rtColors != null)
        DestroyImmediate(rtColors);
      if (rtNormals != null)
        DestroyImmediate(rtNormals);
      for (int i = 0; i < pointSourceBuffers.Length; i++)
      {
        if (pointSourceBuffers[i] != null)
          pointSourceBuffers[i].Dispose();
      }

      if (pcObject != null)
        DestroyImmediate(pcObject);

      isDisposed = true;
    }

    public bool IsDisposed()
    {
      return isDisposed;
    }

  }

}