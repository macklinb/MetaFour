using UnityEngine;

/// <summary>
/// The Billboard class implements the behaviors needed to keep a GameObject
/// oriented towards the user.
/// </summary>
public class Billboard : MonoBehaviour
{
    public enum PivotAxis
    {
        // Rotate about all axes.
        Free,
        // Rotate about an individual axis.
        X,
        Y,
        Z,
    }

    /// <summary>
    /// The axis about which the object will rotate.
    /// </summary>
    [Tooltip("Specifies the axis about which the object will rotate (Free rotates about both X, Y and Z).")]
    public PivotAxis pivotAxis = PivotAxis.Free;

    /// <summary>
    /// Overrides the cached value of the GameObject's default rotation.
    /// </summary>
    public Quaternion DefaultRotation { get; private set; }

    private void Awake()
    {
        // Cache the GameObject's default rotation.
        DefaultRotation = gameObject.transform.rotation;
    }

    /// <summary>
    /// Keeps the object facing the camera.
    /// </summary>
    private void Update()
    {
        // Get a Vector that points from the Camera to the target.
        Vector3 directionToTarget = Camera.main.transform.position - gameObject.transform.position;

        // If we are right next to the camera the rotation is undefined.
        if (directionToTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        // Adjust for the pivot axis.
        switch (pivotAxis)
        {
            case PivotAxis.X:
                directionToTarget.x = gameObject.transform.position.x;
                break;

            case PivotAxis.Y:
                directionToTarget.y = gameObject.transform.position.y;
                break;

            case PivotAxis.Z:
                directionToTarget.z = gameObject.transform.position.y;
                break;

            case PivotAxis.Free:
            default:
                // No changes needed.
                break;
        }

        // Calculate and apply the rotation required to reorient the object and apply the default rotation to the result.
        gameObject.transform.rotation = Quaternion.LookRotation(-directionToTarget) * DefaultRotation;
    }
}