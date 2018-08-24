using UnityEngine;

/// The Billboard class implements the behaviors needed to keep a GameObject
/// oriented towards the user.
public class BillboardMultiple : MonoBehaviour
{
    public bool lockX, lockY, lockZ;

    public Transform[] objects;

    /// Overrides the cached value of the GameObject's default rotation.
    Quaternion defaultRotation;

    void Awake()
    {
        // Cache the GameObject's default rotation.
        defaultRotation = gameObject.transform.rotation;
    }

    /// Keeps the object facing the camera.
    void Update()
    {
        Vector3 delta;

        foreach (Transform obj in objects)
        {
            // Get a Vector that points from the Camera to the target.
            delta = Camera.main.transform.position - obj.position;

             // If we are right next to the camera the rotation is undefined.
            if (Mathf.Abs(delta.sqrMagnitude) < 0.001f)
                continue;

            if (lockX)
                delta.x = 0f;//gameObject.transform.position.x;
            if (lockY)
                delta.y = 0f;//gameObject.transform.position.y;
            if (lockZ)
                delta.z = 0f;//gameObject.transform.position.z;

            // Calculate and apply the rotation required to reorient the object and apply the default rotation to the result.
            obj.rotation = Quaternion.LookRotation(-delta) * defaultRotation;
        }
    }
}