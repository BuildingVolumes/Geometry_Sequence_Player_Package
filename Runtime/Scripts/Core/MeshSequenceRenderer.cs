using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine;

namespace BuildingVolumes.Streaming
{
    public class MeshSequenceRenderer : MonoBehaviour, IMeshSequenceRenderer
    {
        GameObject streamedMeshParent;
        GameObject streamedMeshObject;
        MeshFilter streamedMeshFilter;
        MeshRenderer streamedMeshRenderer;
        Texture2D streamedMeshTexture;

        Material defaultMeshMaterial;
        bool textured;

        private void Awake()
        {
            if (GetComponent<MeshRenderer>())
                GetComponent<MeshRenderer>().enabled = false;
        }

        public bool Setup(Transform parent, SequenceConfiguration config)
        {
            Dispose();

            string name = "MeshSequence";
#if UNITY_EDITOR
            if (!Application.isPlaying)
                name = "Thumbnail";
#endif

            streamedMeshParent = CreateStreamObject(name, parent);
            streamedMeshObject = CreateStreamObject("MeshRenderer", streamedMeshParent.transform);
            ConfigureMeshRenderer(streamedMeshObject, config, true, out streamedMeshRenderer, out streamedMeshFilter, out streamedMeshTexture);

            LoadDefaultMaterials();
            ChangeMaterial(defaultMeshMaterial);

            if (config.textureMode == SequenceConfiguration.TextureMode.None)
                textured = false;
            else
                textured = true;

            return true;
        }

        public void RenderFrame(Frame frame)
        {
            ShowGeometryData(frame, streamedMeshFilter);

            if (textured)
                ShowTextureData(frame, streamedMeshTexture);
        }

        void ShowGeometryData(Frame frame, MeshFilter meshFilter)
        {
            frame.geoJobHandle.Complete();

            VertexAttributeDescriptor vp = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            VertexAttributeDescriptor vt = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);


            if (frame.sequenceConfiguration.hasUVs)
                meshFilter.sharedMesh.SetVertexBufferParams(frame.sequenceConfiguration.maxVertexCount, vp, vt);
            else
                meshFilter.sharedMesh.SetVertexBufferParams(frame.sequenceConfiguration.maxVertexCount, vp);

            meshFilter.sharedMesh.SetIndexBufferParams(frame.sequenceConfiguration.maxIndiceCount, IndexFormat.UInt32);

            meshFilter.sharedMesh.SetVertexBufferData<byte>(frame.vertexBufferRaw, 0, 0, frame.vertexBufferRaw.Length);
            meshFilter.sharedMesh.SetIndexBufferData<byte>(frame.indiceBufferRaw, 0, 0, frame.indiceBufferRaw.Length);
            meshFilter.sharedMesh.SetSubMesh(0, new SubMeshDescriptor(0, frame.sequenceConfiguration.indiceCounts[frame.playbackIndex]), MeshUpdateFlags.DontRecalculateBounds);
            meshFilter.sharedMesh.RecalculateNormals();
        }

        void ShowTextureData(Frame frame, Texture2D texture)
        {
            frame.textureJobHandle.Complete();
            texture.LoadRawTextureData<byte>(frame.textureBufferRaw);
            texture.Apply();
        }

        public void ApplySingleTexture(Frame frame)
        {
            ShowTextureData(frame, streamedMeshRenderer.material.mainTexture as Texture2D);
        }

        GameObject CreateStreamObject(string name, Transform parent)
        {
            GameObject newStreamObject = new GameObject(name);
            newStreamObject.transform.parent = parent;
            newStreamObject.transform.localPosition = Vector3.zero;
            newStreamObject.transform.localRotation = Quaternion.identity;
            newStreamObject.transform.localScale = Vector3.one;
            newStreamObject.hideFlags = HideFlags.DontSave;
            return newStreamObject;
        }

        bool ConfigureMeshRenderer(GameObject renderObject, SequenceConfiguration config, bool hidden, out MeshRenderer renderer, out MeshFilter meshfilter, out Texture2D texture)
        {
            //Configure components
            renderer = renderObject.GetComponent<MeshRenderer>();
            meshfilter = renderObject.GetComponent<MeshFilter>();
            if (!meshfilter)
                meshfilter = renderObject.AddComponent<MeshFilter>();
            if (!meshfilter.sharedMesh)
                meshfilter.sharedMesh = new Mesh();
            if (!renderer)
                renderer = renderObject.AddComponent<MeshRenderer>();

            if (hidden)
            {
                meshfilter.hideFlags = HideFlags.DontSave;
                renderer.hideFlags = HideFlags.DontSave;
            }

            //Configure mesh
            meshfilter.sharedMesh.bounds = config.GetBounds();

            //Configure textures
            if (config.textureMode != SequenceConfiguration.TextureMode.None)
            {
                if (SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.DDS)
                    texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.DXT1, false);
                else if (SequenceConfiguration.GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.ASTC)
                    texture = new Texture2D(config.textureWidth, config.textureHeight, TextureFormat.ASTC_6x6, false);
                else
                {
                    texture = new Texture2D(1, 1);
                    Debug.LogError("Could not determine correct texture format for this platform!");
                }
            }

            else
                texture = new Texture2D(1, 1);

            return true;
        }

        public void ChangeMaterial(Material material)
        {
            ChangeMaterial(material, GeometrySequenceStream.MaterialProperties.Albedo, null);
        }

        public void ChangeMaterial(Material material, GeometrySequenceStream.MaterialProperties properties, List<string> customProperties)
        {
            Material newMat = new Material(material);
            ApplyTextureToMaterial(newMat, streamedMeshTexture, properties, customProperties);
            streamedMeshRenderer.sharedMaterial = newMat;
        }

        void ApplyTextureToMaterial(Material mat, Texture tex, GeometrySequenceStream.MaterialProperties properties, List<string> customProperties)
        {
            if ((GeometrySequenceStream.MaterialProperties.Albedo & properties) == GeometrySequenceStream.MaterialProperties.Albedo)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);
            }

            if ((GeometrySequenceStream.MaterialProperties.Emission & properties) == GeometrySequenceStream.MaterialProperties.Emission)
            {
                if (mat.HasProperty("_EmissionMap"))
                    mat.SetTexture("_EmissionMap", tex);
            }

            if ((GeometrySequenceStream.MaterialProperties.Detail & properties) == GeometrySequenceStream.MaterialProperties.Detail)
            {
                if (mat.HasProperty("_DetailAlbedoMap"))
                    mat.SetTexture("_DetailAlbedoMap", tex);
            }

            if (customProperties != null)
            {
                foreach (string prop in customProperties)
                {
                    if (mat.HasProperty(prop))
                        mat.SetTexture(prop, tex);
                }
            }


            Vector2 scale = mat.GetTextureScale("_MainTex");
            mat.SetTextureScale("_MainTex", new Vector2(scale.x, scale.y * -1));
        }

        public void Show()
        {
            streamedMeshRenderer.enabled = true;
        }
        public void Hide()
        {
            streamedMeshRenderer.enabled = false;
        }

        void LoadDefaultMaterials()
        {
            if (defaultMeshMaterial == null)
                defaultMeshMaterial = Resources.Load("GS_MeshMaterial_Unlit") as Material;
        }

        public void Dispose()
        {
            if (streamedMeshTexture != null)
                DestroyImmediate(streamedMeshTexture);
            

            if (streamedMeshFilter != null)
                DestroyImmediate(streamedMeshFilter.sharedMesh);
            

            if (streamedMeshObject != null)       
                DestroyImmediate(streamedMeshObject);      


            if (streamedMeshParent != null)
                DestroyImmediate(streamedMeshParent);
        }

        public void EndEditorLife()
        {

        }
    }
}