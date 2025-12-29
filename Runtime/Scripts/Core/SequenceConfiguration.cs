using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
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
    public List<float> boundsCenter;
    public NativeArray<float> boundsCenterNative; // we need the Native version to be able to pass to Jobs 
    public List<float> boundsSize;
    public NativeArray<float> boundsSizeNative; // we need the Native version to be able to pass to Jobs 
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
        var boundsC = configuration.boundsCenter;
        configuration.boundsCenterNative = new NativeArray<float>(3, Allocator.Persistent);
        configuration.boundsCenterNative[0] = boundsC[0];
        configuration.boundsCenterNative[1] = boundsC[1];
        configuration.boundsCenterNative[2] = boundsC[2];
        var boundsS = configuration.boundsSize;
        configuration.boundsSizeNative = new NativeArray<float>(3, Allocator.Persistent);
        configuration.boundsSizeNative[0] = boundsS[0];
        configuration.boundsSizeNative[1] = boundsS[1];
        configuration.boundsSizeNative[2] = boundsS[2];
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
      bool usesDeprecatedBounds = boundsCenter is not { Count: 3 };
      
      if (usesDeprecatedBounds)
      {
        Debug.LogWarning("Sequence was created with a deprecated version of the Converter tool. Please update the converter tool and re-convert your sequence: " + "https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/releases/");
        return new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
      }

      Vector3 center = new Vector3(boundsCenter[0], boundsCenter[1], boundsCenter[2]);
      Vector3 size = new Vector3(boundsSize[0], boundsSize[1], boundsSize[2]);
      return new Bounds(center, size);


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


