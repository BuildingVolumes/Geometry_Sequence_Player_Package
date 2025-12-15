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
    public List<float> boundsCenter;
    public List<float> boundsSize;
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
      bool usesDeprecatedBounds = true;

      if (boundsCenter != null)
        if (boundsCenter.Count == 3)
          usesDeprecatedBounds = false;

      if (usesDeprecatedBounds)
      {
        Debug.LogWarning("Sequence was created with a deprecated version of the Converter tool. Please update the converter tool and re-convert your sequence: " + "https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/releases/");
        return new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
      }

      else
      {
        Vector3 center = new Vector3(boundsCenter[0], boundsCenter[1], boundsCenter[2]);
        Vector3 size = new Vector3(boundsSize[0], boundsSize[1], boundsSize[2]);
        return new Bounds(center, size);
      }


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


