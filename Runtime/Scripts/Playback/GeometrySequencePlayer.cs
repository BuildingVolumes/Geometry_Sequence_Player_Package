using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace BuildingVolumes.Streaming
{
    public class GeometrySequencePlayer : MonoBehaviour
    {
        GeometrySequenceStream stream;

        [SerializeField]
        string relativePath = "";
        [SerializeField]
        string absolutePath = "";
        [SerializeField]
        GeometrySequenceStream.PathType pathRelation;

        [SerializeField]
        bool playAtStart = true;
        [SerializeField]
        bool loopPlay = true;
        [SerializeField]
        float playbackFPS = 30;

        public UnityEvent<GeometrySequencePlayer, GSPlayerEvents> playbackEvents = new UnityEvent<GeometrySequencePlayer, GSPlayerEvents>();

        float playbackTimeMs = 0;
        bool play = false;
        bool bufferingSequence = true;

        public enum GSPlayerEvents { BufferingStarted, BufferingCompleted, PlaybackStarted, PlaybackFinished, Looped, FrameDropped }

        // Start is called before the first frame update
        void Start()
        {
            SetupGeometryStream();
            OpenSequence(relativePath, pathRelation, playbackFPS, playAtStart);
        }

        public void SetupGeometryStream()
        {
            //Add a Geometry Sequence Stream if there is non already existing on this gameobject
            if (stream == null)
            {
                stream = gameObject.GetComponent<GeometrySequenceStream>();
                if (stream == null)
                    stream = gameObject.AddComponent<GeometrySequenceStream>();
            }
        }

        public void ShowThumbnail(string pathToSequence)
        {
            if (stream == null)
                stream = GetComponent<GeometrySequenceStream>();

            stream.LoadEditorThumbnail(pathToSequence);
        }

        public void ClearThumbnail()
        {
            if (stream == null)
                stream = GetComponent<GeometrySequenceStream>();

            stream.ClearEditorThumbnail();
        }

        private void Update()
        {
            if (play)
            {
                playbackTimeMs += Time.deltaTime * 1000;

                if (GetCurrentTime() >= GetTotalTime())
                {
                    if (!loopPlay)
                    {
                        Stop();
                        playbackEvents.Invoke(this, GSPlayerEvents.PlaybackFinished);
                    }

                    else
                    {
                        GoToTime(0);
                        playbackEvents.Invoke(this, GSPlayerEvents.Looped);
                    }
                }

                stream.UpdateFrame(playbackTimeMs);

                if (GetFrameDropped())
                    playbackEvents.Invoke(this, GSPlayerEvents.FrameDropped);
            }
        }

        //+++++++++++++++++++++ PLAYBACK API ++++++++++++++++++++++++

        /// <summary>
        /// Load a .ply sequence (and optionally textures) from the path, and start playback if autoplay is enabled.
        /// Returns false when sequence could not be loaded, see Unity Console output for details in this case
        /// </summary>
        /// <param name="path">The path to the sequence</param>
        /// <param name="relativeTo">Which location is the path relative to?</param>
        /// <param name="playbackFPS">Desired playback framerate (Default = 30 fps)</param>
        /// <param name="autoplay">Start the playback as soon as possible. Might have a small delay as a frames need to load first</param>
        /// <param name="buffer">Preload the first few frames into memory. Only applicable if autostart set to false. <</param>
        /// <returns>True when the sequence could sucessfully be loaded, false if an error has occured</returns>
        public bool OpenSequence(string path, GeometrySequenceStream.PathType relativeTo, float playbackFPS = 30f, bool autoplay = false, bool buffer = true)
        {
            if (path.Length > 0)
            {
                this.playbackFPS = playbackFPS;
                SetPath(path, relativeTo);
                return LoadCurrentSequence(autoplay, buffer);
            }

            return false;
        }

        /// <summary>
        /// Loads the sequence which is currently set in the player, optionally starts playback.
        /// </summary>
        /// <param name="autoplay">Start playback immediatly after loading</param>
        /// <param name="buffer">If the playback does not autostart, loads the first few frames into memory</param>
        /// <returns>True when the sequence could sucessfully be reloaded, false if an error has occured</returns>
        public bool LoadCurrentSequence(bool autoplay = false, bool buffer = true)
        {
            bool sucess = stream.ChangeSequence(absolutePath, playbackFPS);

            if (sucess)
            {
                if (buffer || autoplay)
                {
                    playbackEvents.Invoke(this, GSPlayerEvents.BufferingStarted);
                    stream.bufferedReader.BufferFrames(0);
                    StartCoroutine(WaitForBufferingCompleted(autoplay));
                }
            }

            return sucess;
        }


        /// <summary>
        /// Set a new path in the player, but don't load the sequence. Use LoadCurrentSequence() to actually load it, or OpenSequence() to set and load a sequence.
        /// </summary>
        /// <param name="path">The relative or absolute path to the new Sequence</param>
        /// <param name="relativeTo">Specifiy to which path your sequence path is relative, or if it is an absolute path</param>
        public void SetPath(string path, GeometrySequenceStream.PathType relativeTo)
        {
            if (path.Length < 1)
                return;

            this.relativePath = path;
            pathRelation = relativeTo;
            Pause();

            //Set the correct absolute path depending on the files location
            if (pathRelation == GeometrySequenceStream.PathType.RelativeToDataPath)
                absolutePath = Path.Combine(UnityEngine.Application.dataPath, this.relativePath);

            if (pathRelation == GeometrySequenceStream.PathType.RelativeToStreamingAssets)
                absolutePath = Path.Combine(UnityEngine.Application.streamingAssetsPath, this.relativePath);

            if (pathRelation == GeometrySequenceStream.PathType.RelativeToPersistentDataPath)
                absolutePath = Path.Combine(UnityEngine.Application.persistentDataPath, this.relativePath);

            if (pathRelation == GeometrySequenceStream.PathType.AbsolutePath)
                absolutePath = this.relativePath;

            return;
        }

        /// <summary>
        /// Start Playback from the current location
        /// </summary>
        public void Play()
        {
            play = true;
        }

        /// <summary>
        /// Pause current playback
        /// </summary>
        public void Pause()
        {
            play = false;
        }

        /// <summary>
        /// Stops the playback
        /// </summary>
        public void Stop()
        {
            Pause();
            GoToFrame(0);
            stream.DisposeDisplayedGeometry();
        }

        /// <summary>
        /// Activate or deactivate looped playback
        /// </summary>
        /// <param name="enabled"></param>
        public void SetLoopPlay(bool enabled)
        {
            loopPlay = enabled;
        }

        /// <summary>
        /// Activate or deactivate automatic playback (when the scene starts)
        /// </summary>
        /// <param name="enabled"></param>
        public void SetAutoStart(bool enabled)
        {
            playAtStart = false;
        }

        /// <summary>
        /// Seeks to the start of the sequence and then starts playback
        /// </summary>
        public bool PlayFromStart()
        {
            if (GoToFrame(0))
            {
                Play();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Goes to a specific frame. Use GetTotalFrames() to check how many frames the clip contains
        /// </summary>
        /// <param name="frame"></param>
        public bool GoToFrame(int frame)
        {
            if (stream != null)
            {
                float time = (frame * stream.targetFrameTimeMs) / 1000;
                return GoToTime(time);
            }

            return false;
        }

        /// <summary>
        /// Goes to a specific time in  a clip. The time is dependent on the framerate e.g. the same clip at 30 FPS is twice as long as at 60 FPS.
        /// </summary>
        /// <param name="timeInSeconds"></param>
        /// <returns></returns>
        public bool GoToTime(float timeInSeconds)
        {
            if (timeInSeconds < 0 || timeInSeconds > GetTotalTime())
                return false;

            playbackTimeMs = timeInSeconds * 1000;

            if (!play)
            {
                stream.UpdateFrame(playbackTimeMs);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Makes the sequence visible on the screen
        /// </summary>
        public void Show()
        {
            if (stream.streamedMeshRenderer != null)
                stream.streamedMeshRenderer.enabled = true;
        }

        /// <summary>
        /// Hides the sequence from the screen, but still lets it runnning in the background
        /// </summary>
        public void Hide()
        {
            if (stream.streamedMeshRenderer != null)
                stream.streamedMeshRenderer.enabled = false;
        }


        /// <summary>
        /// Gets the absolute path to the folder containing the sequence
        /// </summary>
        /// <returns></returns>
        public string GetAbsoluteSequencePath()
        {
            return absolutePath;
        }

        /// <summary>
        /// Get's the relative path to the sequence directory. Get the path which it is relative to with GetRelativeTo()
        /// </summary>
        /// <returns></returns>
        public string GetRelativeSequencePath()
        {
            return relativePath;
        }

        /// <summary>
        /// Get the location to to which the relativePath is relative to.
        /// </summary>
        /// <returns>The relative location.</returns>
        public GeometrySequenceStream.PathType GetRelativeTo()
        {
            return pathRelation;
        }

        /// <summary>
        /// Is the current clip playing?
        /// </summary>
        /// <returns></returns>
        public bool IsPlaying()
        {
            return play;
        }

        /// <summary>
        /// Is looped playback enabled?
        /// </summary>
        /// <returns></returns>
        public bool GetLoopingEnabled()
        {
            return loopPlay;
        }

        /// <summary>
        /// At which frame is the playback currently?
        /// </summary>
        /// <returns></returns>
        public int GetCurrentFrameIndex()
        {
            if (stream != null)
                return stream.currentFrameIndex;
            return -1;
        }

        /// <summary>
        /// At which time is the playback currently in seconds?
        /// Note that the time is dependent on the framerate e.g. the same clip at 30 FPS is twice as long as at 60 FPS.
        /// </summary>
        /// <returns></returns>
        public float GetCurrentTime()
        {
            return playbackTimeMs / 1000;
        }

        /// <summary>
        /// How many frames are there in total in the whole sequence?
        /// </summary>
        /// <returns></returns>
        public int GetTotalFrames()
        {
            if (stream != null)
                if (stream.bufferedReader != null)
                    return stream.bufferedReader.totalFrames;
            return -1;
        }

        /// <summary>
        /// How long is the sequence in total?
        /// Note that the time is dependent on the framerate e.g. the same clip at 30 FPS is twice as long as at 60 FPS.
        /// </summary>
        /// <returns></returns>
        public float GetTotalTime()
        {
            return GetTotalFrames() / GetTargetFPS();
        }

        /// <summary>
        /// The target fps is the framerate we _want_ to achieve in playback. However, this is not guranteed, if system resources
        /// are too low. Use GetActualFPS() to see if you actually achieve this framerate
        /// </summary>
        /// <returns></returns>
        public float GetTargetFPS()
        {
            if (stream != null)
                return 1000 / stream.targetFrameTimeMs;
            return -1;
        }

        /// <summary>
        /// What is the actual current playback framerate? If the framerate is much lower than the target framerate,
        /// consider reducing the complexity of your sequence, and don't forget to disable any V-Sync (VSync, FreeSync, GSync) methods!
        /// </summary>
        /// <returns></returns>
        public float GetActualFPS()
        {
            if (stream != null)
                return stream.smoothedFPS;
            return -1;
        }

        /// <summary>
        /// Check if there have been framedrops since you last checked this function
        /// Too many framedrops mean the system can't keep up with the playback
        /// and you should reduce your Geometric complexity or framerate
        /// </summary>
        /// <returns></returns>
        public bool GetFrameDropped()
        {
            if (stream != null)
            {
                bool dropped = stream.frameDropped;
                stream.frameDropped = false;
                return dropped;
            }

            return false;
        }

        /// <summary>
        /// Checks if the playback cache has been filled and is ready to play.
        /// If it is, playback starts immediatly once you call Play()
        /// </summary>
        /// <returns></returns>
        public bool GetCacheFilled()
        {
            if (stream != null)
            {
                if (stream.bufferedReader != null)
                {
                    bool cacheReady = true;

                    for (int i = 0; i < stream.bufferedReader.frameBuffer.Length; i++)
                    {
                        if (!stream.bufferedReader.IsFrameBuffered(stream.bufferedReader.frameBuffer[i]))
                            cacheReady = false;
                    }

                    return cacheReady;
                }
            }

            return false;
        }


        #region Events

        private IEnumerator WaitForBufferingCompleted(bool playFromStart)
        {
            if (bufferingSequence)
            {
                while (!GetCacheFilled())
                    yield return null;

                bufferingSequence = false;

                playbackEvents.Invoke(this, GSPlayerEvents.BufferingCompleted);

                if (playFromStart)
                {
                    PlayFromStart();
                    playbackEvents.Invoke(this, GSPlayerEvents.PlaybackStarted);
                }
            }

        }

        #endregion



        #region Obsolete

        [Obsolete("LoadSequence is deprecated, please use OpenSequence instead.", true)]
        public void LoadSequence()
        { }

        [Obsolete("ReloadSequence is deprecated, please use LoadCurrentSequence instead.", true)]
        public void ReloadSequence()
        { }

        #endregion
    }


    //Thumbnails are dynamically loaded each time the scene is opened, so that they 
    //won't need to be saved in the scene, which might make the scene file huge
    //This class helps to detect when a thumbnail load is neccessary
    [InitializeOnLoad]
    public class ThumbnailLoadHelper
    {
        static bool LoadThumbnailAfterEditorOpen = false;

        static ThumbnailLoadHelper()
        {
            EditorSceneManager.sceneOpened += LoadThumbnailOnSceneOpen;
            EditorSceneManager.sceneClosing += ClearThumbnailOnSceneClosed;
            EditorSceneManager.sceneClosing += (Scene s, bool b) => ManageThumbnailRenderSubscription(false);

            //Trick to load thumbnail the first time, and only the first time, after Unity has started.
            //We subscribe to EditorApplication.update instead of calling it directly, so that the 
            //LoadThumbDelayed function only gets called after everything has been loaded
            if (!SessionState.GetBool("GSSThumbLoadedFirstTime", false))
            {
                EditorApplication.update += LoadThumbDelayed;
                LoadThumbnailAfterEditorOpen = true;
            }

            //After every recompile, the subscription to EditorApplication.Update gets broken
            //So we need to re-subscribe after every recompile
            ManageThumbnailRenderSubscription(true);

        }

        static void LoadThumbnailOnSceneOpen(Scene scene1, OpenSceneMode mode)
        {
            GeometrySequencePlayer[] players = GameObject.FindObjectsOfType<GeometrySequencePlayer>();

            foreach (GeometrySequencePlayer player in players)
            {
                if (player.GetRelativeSequencePath() != null)
                {
                    if (player.GetRelativeSequencePath().Length > 0)
                    {
                        player.ShowThumbnail(player.GetRelativeSequencePath());
                    }
                }
            }
        }

        static void ClearThumbnailOnSceneClosed(Scene scene1, bool removingScene)
        {
            GeometrySequencePlayer[] players = GameObject.FindObjectsOfType<GeometrySequencePlayer>();

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
                Scene nullScene = new Scene();
                LoadThumbnailOnSceneOpen(nullScene, OpenSceneMode.Single);
                SessionState.SetBool("GSSThumbLoadedFirstTime", true);
            }
        }

        static void ManageThumbnailRenderSubscription(bool subscribe)
        {
            PointcloudRenderer[] renderers = GameObject.FindObjectsOfType<PointcloudRenderer>();

            foreach (PointcloudRenderer renderer in renderers)
            {
                if (subscribe)
                    renderer.SubscribeToEditorUpdate();
                else
                    renderer.UnsubscribeFromEditorUpdate();
            }
        }



    }


}
