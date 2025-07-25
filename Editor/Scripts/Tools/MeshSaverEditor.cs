//MeshSaverEditor by pharan at Github: https://github.com/pharan/Unity-MeshSaver/tree/master
//Licensed under the MIT License

using UnityEditor;
using UnityEngine;

namespace BuildingVolumes.Player
{
    public static class MeshSaverEditor
    {

        [MenuItem("CONTEXT/MeshFilter/Save Mesh...")]
        public static void SaveMeshInPlace(MenuCommand menuCommand)
        {
            MeshFilter mf = menuCommand.context as MeshFilter;
            Mesh m = mf.sharedMesh;
            SaveMesh(m, m.name, false);
        }

        [MenuItem("CONTEXT/MeshFilter/Save Mesh As New Instance...")]
        public static void SaveMeshNewInstanceItem(MenuCommand menuCommand)
        {
            MeshFilter mf = menuCommand.context as MeshFilter;
            Mesh m = mf.sharedMesh;
            SaveMesh(m, m.name, true);
        }

        public static void SaveMesh(Mesh mesh, string name, bool makeNewInstance)
        {
            string path = EditorUtility.SaveFilePanel("Save Separate Mesh Asset", "Assets/", name, "asset");
            if (string.IsNullOrEmpty(path)) return;

            path = FileUtil.GetProjectRelativePath(path);

            Mesh meshToSave = (makeNewInstance) ? Object.Instantiate(mesh) as Mesh : mesh;

            AssetDatabase.CreateAsset(meshToSave, path);
            AssetDatabase.SaveAssets();
        }

    }
}


