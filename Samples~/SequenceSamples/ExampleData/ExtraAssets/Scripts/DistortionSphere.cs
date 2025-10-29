using UnityEngine;

[ExecuteInEditMode]

public class DistortionSphere : MonoBehaviour
{
  public Transform distortionSphere;
  public Material distortionMaterial;

  //This small script just sets the position and scale parameters in the Distortion Material, based on the Distortion Sphere Game Object
  void Update()
  {
    if(distortionSphere && distortionMaterial)
    {
      distortionMaterial.SetVector("_DistortionPosition", distortionSphere.position);
      distortionMaterial.SetFloat("_DistortionRadius", distortionSphere.lossyScale.x);
    }

  }
}
