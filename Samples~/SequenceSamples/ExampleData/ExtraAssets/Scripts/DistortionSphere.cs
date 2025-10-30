using BuildingVolumes.Player;
using UnityEngine;

[ExecuteInEditMode]
public class DistortionSphere : MonoBehaviour
{
  public Transform distortionSphere;
  public GeometrySequenceStream stream;

  //This small script just sets the position and scale parameters in the Distortion Material, based on the Distortion Sphere Game Object
  void Update()
  {
    if(distortionSphere && stream)
    {
      if(stream.customMaterial != null)
      {
        stream.customMaterial.SetVector("_DistortionPosition", transform.InverseTransformPoint(distortionSphere.position));
        stream.customMaterial.SetFloat("_DistortionRadius", distortionSphere.lossyScale.x);
      }
    }
  }
}
