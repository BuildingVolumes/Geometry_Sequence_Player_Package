using UnityEngine;
using Unity.Collections;

namespace BuildingVolumes.Streaming
{
    public interface IPointCloudRenderer
    {
        public void Setup(SequenceConfiguration configuration, Transform parent, float pointSize, GeometrySequenceStream.PointType pointType);
        public void RenderFrame(Frame frame);
        public void SetPointSize(float size);
        public void SetPointcloudMaterial(GeometrySequenceStream.PointType type);
        public void Show();
        public void Hide();
        public void Dispose();
    }
}

