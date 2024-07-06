using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BuildingVolumes.Streaming
{
    public class SequenceConfiguration
    {
        public enum GeometryType {point = 0, mesh = 1, texturedMesh = 2};
        public enum TextureMode {None = 0, single = 1, PerFrame = 2};

        public GeometryType geometryType;
        public TextureMode textureMode;
        public bool hasUVs;
        public int maxVertexCount;
        public int maxIndiceCount;
        public List<float> maxBounds;
        public int textureWidth;
        public int textureHeight;
        public int textureSize;
        public List<int> headerSizes;
        public List<int> verticeCounts;
        public List<int> indiceCounts;



        public static SequenceConfiguration LoadConfigFromFile(string pathToSequenceDir)
        {
            string content = "";

            string pathToFile = pathToSequenceDir + "/" + "sequence.json";

            if (File.Exists(pathToFile))
            {
                content = File.ReadAllText(pathToFile);
            }

            else
            {
                Debug.LogError("Could not find sequence.json metadata file!");
                return null;
            }

            SequenceConfiguration configuration = new SequenceConfiguration();

            try
            {
                configuration = JsonUtility.FromJson<SequenceConfiguration>(content);
            }

            catch(Exception e)
            {
                Debug.LogError("Could not parse metadata file! " + e.Message);
                return null;
            }

            if(configuration.headerSizes.Count == 0 || configuration.verticeCounts.Count == 0)
            {
                Debug.LogError("Metadata file invalid!");
                return null;
            }

            return configuration;
        }

        public Bounds GetBounds()
        {
            Vector3 center = Vector3.zero;
            center.x = maxBounds[0] + maxBounds[3];
            center.y = maxBounds[1] + maxBounds[4];
            center.z = maxBounds[2] + maxBounds[5];

            Vector3 size = Vector3.zero;
            size.x = Mathf.Abs(maxBounds[0]) + Mathf.Abs(maxBounds[3]);
            size.y = Mathf.Abs(maxBounds[1]) + Mathf.Abs(maxBounds[4]);
            size.z = Mathf.Abs(maxBounds[2]) + Mathf.Abs(maxBounds[5]);

            return new Bounds(center, size);
        }

    }
}


