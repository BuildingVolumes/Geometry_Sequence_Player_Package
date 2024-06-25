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

            return configuration;
        }

    }
}


