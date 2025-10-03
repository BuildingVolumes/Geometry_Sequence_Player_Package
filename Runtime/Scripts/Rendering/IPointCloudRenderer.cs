using UnityEngine;
using Unity.Collections;

namespace BuildingVolumes.Player
{
  public interface IPointCloudRenderer
  {
    public void Setup(SequenceConfiguration configuration, Transform parent, float pointSize, float emission, Material pointcloudMaterial);
    public void SetFrame(Frame frame);
    public void SetPointSize(float size);
    public void SetPointEmission(float emission);
    public void SetPointcloudMaterial(Material pointcloudMaterial);
    public void SetPointcloudMaterial(Material pointcloudMaterial, float pointSize, float pointEmission);

    public void Show();
    public void Hide();
    public void Dispose();
  }
}

