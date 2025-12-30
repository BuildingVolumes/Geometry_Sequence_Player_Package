using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BuildingVolumes.Player
{
  public class SequenceConfiguration
  {
    public enum GeometryType { point = 0, mesh = 1, texturedMesh = 2 };
    public enum TextureMode { None = 0, Single = 1, PerFrame = 2 };
    public enum TextureFormat { NotSupported = 0, DDS = 1, ASTC = 2 };

    public GeometryType geometryType;
    public TextureMode textureMode;
    public bool DDS;
    public bool ASTC;
    public bool hasUVs;
    public bool hasNormals = false;
    public bool hasAlpha = true;
    public bool halfPrecision;
    public int maxVertexCount;
    public int maxIndiceCount;
    public Vector3 boundsCenter;
    public Vector3 boundsSize;
    public int textureWidth;
    public int textureHeight;
    public int textureSizeDDS;
    public int textureSizeASTC;
    public List<int> headerSizes;
    public List<int> verticeCounts;
    public List<int> indiceCounts;


    public static SequenceConfiguration LoadConfigFromFile(string path)
    {
      string content;

      path += "/sequence.json";

      if (File.Exists(path) && path.Length > 0)
      {
        content = File.ReadAllText(path);
      }

      else
      {
        Debug.LogError("Could not find sequence.json metadata file at: " + path);
        return null;
      }

      SequenceConfiguration configuration;

      try
      {
        configuration = JsonUtility.FromJson<SequenceConfiguration>(content);
      }

      catch (Exception e)
      {
        Debug.LogError("Could not parse metadata file! " + e.Message);
        return null;
      }

      if (configuration.headerSizes.Count == 0 || configuration.verticeCounts.Count == 0)
      {
        Debug.LogError("Metadata file invalid!");
        return null;
      }

      return configuration;
    }

    public Bounds GetBounds()
    {

      return new Bounds(boundsCenter, boundsSize);

    }

    public static TextureFormat GetDeviceDependentTextureFormat()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
      return TextureFormat.DDS;
#elif UNITY_IOS || UNITY_ANDROID || UNITY_TVOS || UNITY_VISIONOS
            return TextureFormat.ASTC;
#else
            return TextureFormat.NotSupported;
#endif
    }
  }
}


