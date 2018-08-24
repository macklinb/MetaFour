using UnityEngine;

// Disables the Meta device depending the value of NetworkSettings.Current.metaEnabled
public class MetaDisabler : MonoBehaviour
{
    public GameObject standardRigPrefab;
    public GameObject[] metaObjects;

    public const bool DEFAULT_META_ENABLED = true;

    bool metaObjectsDestroyed = false;

    void Awake()
    {
        if (Settings.Current.metaEnabled)
            EnableMeta();
        else
            DisableMeta();
    }

    void Start()
    {
        // If the Meta isn't available/connected at this point, delete all the Meta-related objects in the scene, and spawn a standard camera rig
        if (!metaObjectsDestroyed && !IsMetaAvailable())
        {
            DisableMeta();
        }
    }
    
    void DisableMeta()
    {
        Debug.Log("MetaDisabler : Disabling Meta");

        // Destroy current meta objects
        for (int i = 0; i < metaObjects.Length; i++)
            DestroyImmediate(metaObjects[i]);

        // Instantiate a standard camera rig
        var rig = GameObject.Instantiate(standardRigPrefab, Vector3.zero, Quaternion.identity, this.transform);
        rig.name = standardRigPrefab.name.Replace(" (Clone)", "");

        // Enable vsync
        QualitySettings.vSyncCount = 1;

        metaObjectsDestroyed = true;
    }

    void EnableMeta()
    {
        foreach (var obj in metaObjects)
            obj.SetActive(true);
    }

    bool IsMetaAvailable()
    {
        // Check existence of DLL's
        if (LoadLibrary(Meta.Interop.DllReferences.MetaCore) != System.IntPtr.Zero &&
            LoadLibrary(Meta.Interop.DllReferences.MetaUnity) != System.IntPtr.Zero)
        {
            // Check status of connection
            var connectionStatus = Meta.Plugin.SystemApi.GetDeviceStatus().StatusOfConnection;

            Debug.Log("MetaDisabler : IsMetaAvailable - Meta ConnectionStatus = " + connectionStatus);

            return (connectionStatus == Meta.Plugin.DeviceStatusSnapshot.ConnectionStatus.CONNECTED);
        }

        Debug.LogError("MetaDisabler : IsMetaAvailable  - Meta DLL's not found!");

        return false;
    }

    [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
    static extern System.IntPtr LoadLibrary(string fileName);
}
