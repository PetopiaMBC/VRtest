using UnityEngine.XR.ARFoundation;
using UnityEngine;

/// <summary>Keeps one controller XR rig alive across Adoption and gameplay scenes.</summary>
[DisallowMultipleComponent]
public sealed class PersistentXRPlayerRig : MonoBehaviour
{
    private static PersistentXRPlayerRig instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        DisableTemplateLocomotion();

        Camera xrCamera = GetComponentInChildren<Camera>(true);
        if (xrCamera == null)
            return;

        ARCameraManager cameraManager = xrCamera.GetComponent<ARCameraManager>();
        if (cameraManager == null)
            cameraManager = xrCamera.gameObject.AddComponent<ARCameraManager>();

        // Adoption/Q2 are opaque. The Q3 wrapper turns the Meta OpenXR camera on.
        cameraManager.enabled = false;
    }

    private void DisableTemplateLocomotion()
    {
        Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
                continue;

            string typeName = behaviour.GetType().FullName;
            bool isXriLocomotion = !string.IsNullOrEmpty(typeName) &&
                                   typeName.StartsWith(
                                       "UnityEngine.XR.Interaction.Toolkit.Locomotion.",
                                       System.StringComparison.Ordinal);
            bool isTemplateMoveProvider = typeName ==
                "UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.DynamicMoveProvider";
            if (isXriLocomotion || isTemplateMoveProvider)
                behaviour.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
