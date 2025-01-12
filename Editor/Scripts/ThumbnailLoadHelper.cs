#if UNITY_EDITOR

using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEditor.Callbacks;

namespace BuildingVolumes.Streaming
{
    //Thumbnails are dynamically loaded each time the scene is opened, so that they 
    //won't need to be saved in the scene file.
    //This class helps to detect when a thumbnail load is neccessary

    [InitializeOnLoad]
    public class ThumbnailLoadHelper
    {
        static bool LoadThumbnailAfterEditorOpen = false;

        static ThumbnailLoadHelper()
        {
            EditorSceneManager.sceneOpened += LoadThumbnailOnSceneOpen;
            EditorSceneManager.sceneClosing += ClearThumbnailOnSceneClosed;
            EditorApplication.playModeStateChanged += LoadThumbnailOnExitPlaymode;

            //Trick to load thumbnail the first time, and only the first time, after Unity has started.
            //We subscribe to EditorApplication.update instead of calling it directly, so that the 
            //LoadThumbDelayed function only gets called after everything has been loaded
            if (!SessionState.GetBool("GSSThumbLoadedFirstTime", false))
            {
                EditorApplication.update += LoadThumbDelayed;
                LoadThumbnailAfterEditorOpen = true;
            }

            //After every recompile, the scripts get reset
            //So we need to re-load the thumbnail after every recompile
            LoadThumbnail();
        }

        static void LoadThumbnailOnSceneOpen(UnityEngine.SceneManagement.Scene scene1, OpenSceneMode mode)
        {
            LoadThumbnail();
        }

        //Load thumbnail after build has finished
        [PostProcessBuildAttribute(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            EditorApplication.update += LoadThumbDelayed;
            LoadThumbnailAfterEditorOpen = true;
        }

        static void LoadThumbnail()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            GeometrySequencePlayer[] players = GameObject.FindObjectsByType<GeometrySequencePlayer>(FindObjectsSortMode.None);

            foreach (GeometrySequencePlayer player in players)
            {
                if (player.GetAbsoluteSequencePath() != null)
                {
                    if (player.GetAbsoluteSequencePath().Length > 0)
                    {
                        player.ShowThumbnail(player.GetAbsoluteSequencePath());

                        //Before a domain reload destroys the thumbnail data, we need to free the memory
                        AssemblyReloadEvents.beforeAssemblyReload += ClearThumbnail;
                    }
                }
            }
        }

        static void ClearThumbnailOnSceneClosed(UnityEngine.SceneManagement.Scene scene1, bool removingScene)
        {
            if (EditorApplication.isPlaying)
                return;

            ClearThumbnail();
        }

        static void LoadThumbnailOnExitPlaymode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                LoadThumbnail();
        }

        static void ClearThumbnail()
        {
            GeometrySequencePlayer[] players = GameObject.FindObjectsByType<GeometrySequencePlayer>(FindObjectsSortMode.None);

            foreach (GeometrySequencePlayer player in players)
            {
                player.ClearThumbnail();
            }
        }

        static void LoadThumbDelayed()
        {
            if (LoadThumbnailAfterEditorOpen)
            {
                LoadThumbnailAfterEditorOpen = false;
                EditorApplication.update -= LoadThumbDelayed;
                LoadThumbnailOnSceneOpen(new UnityEngine.SceneManagement.Scene(), OpenSceneMode.Single);
                SessionState.SetBool("GSSThumbLoadedFirstTime", true);
            }
        }
    }
}


#endif

