using UnityEngine;

public class MeshletCreator : MonoBehaviour
{
    [SerializeField]
    int quadCount = 2000;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("No mesh filter found!");
            return;
        }

        Vector3 vertice1 = new Vector3(-0.5f, 0.5f, 0f);
        Vector3 vertice2 = new Vector3(0.5f, 0.5f, 0f);
        Vector3 vertice3 = new Vector3(0.5f, -0.5f, 0f);
        Vector3 vertice4 = new Vector3(-0.5f, -0.5f, 0f);

        Vector2 uv1 = new Vector2(0, 1);
        Vector2 uv2 = new Vector2(1, 1);
        Vector2 uv3 = new Vector2(1, 0);
        Vector2 uv4 = new Vector2(0, 0);

        Mesh meshlet = new Mesh();

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

        meshlet.vertices = vertices;
        meshlet.triangles = indices;
        meshlet.SetUVs(0, uvs);
        meshlet.RecalculateBounds();

        meshFilter.sharedMesh = meshlet;

    }
}
