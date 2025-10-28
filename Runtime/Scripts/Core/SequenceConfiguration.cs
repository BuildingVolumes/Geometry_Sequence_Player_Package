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
    public int maxVertexCount;
    public int maxIndiceCount;
    public List<float> maxBounds;
    public int textureWidth;
    public int textureHeight;
    public int textureSizeDDS;
    public int textureSizeASTC;
    public List<int> headerSizes;
    public List<int> verticeCounts;
    public List<int> indiceCounts;


    public static SequenceConfiguration LoadConfigFromFile(string path)
    {
      string content = "";

      path = path + "/sequence.json";

      if (File.Exists(path) && path.Length > 0)
      {
        content = File.ReadAllText(path);
      }

      else
      {
        Debug.LogError("Could not find sequence.json metadata file at: " + path);
        return null;
      }

      SequenceConfiguration configuration = new SequenceConfiguration();

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
      Vector3 center = Vector3.zero;
      center.x = (maxBounds[0] + maxBounds[3]) / 2;
      center.y = (maxBounds[1] + maxBounds[4]) / 2;
      center.z = (maxBounds[2] + maxBounds[5]) / 2;

      Vector3 size = Vector3.zero;
      size.x = Mathf.Abs(maxBounds[0]) + Mathf.Abs(maxBounds[3]);
      size.y = Mathf.Abs(maxBounds[1]) + Mathf.Abs(maxBounds[4]);
      size.z = Mathf.Abs(maxBounds[2]) + Mathf.Abs(maxBounds[5]);

      return new Bounds(center, size);
    }

    static public TextureFormat GetDeviceDependentTextureFormat()
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


