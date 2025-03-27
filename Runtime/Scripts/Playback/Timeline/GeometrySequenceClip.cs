using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace BuildingVolumes.Player
{
    public class GeometrySequenceClip : PlayableAsset
    {
        public GeometrySequenceStream stream;
        public string relativePath;
        public GeometrySequenceStream.PathType pathRelation;
        public float targetPlaybackFPS = 30;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<GeometrySequenceBehaviour> playable = ScriptPlayable<GeometrySequenceBehaviour>.Create(graph);

            GeometrySequenceBehaviour geoSequenceBehaviour = playable.GetBehaviour();
            stream = owner.GetComponent<GeometrySequenceStream>();
            geoSequenceBehaviour.relativePath = relativePath;
            geoSequenceBehaviour.pathRelation = pathRelation;
            geoSequenceBehaviour.targetPlaybackFPS = targetPlaybackFPS;

            return playable;
        }

        #region Thumbnail
#if UNITY_EDITOR

        public void ShowThumbnail(string path)
        {
            if (stream != null)
                stream.LoadEditorThumbnail(path);
        }

        public void ClearThumbnail()
        {
            if (stream != null)
                stream.ClearEditorThumbnail();
        }

#endif
        #endregion
    }
}

