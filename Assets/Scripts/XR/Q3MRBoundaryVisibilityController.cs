using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.NativeTypes;

/// <summary>Suppresses the guardian boundary only while Q3 passthrough is tracking.</summary>
[DisallowMultipleComponent]
public sealed class Q3MRBoundaryVisibilityController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float retryInterval = 0.5f;

    private BoundaryVisibilityFeature feature;
    private ARCameraManager cameraManager;
    private XrBoundaryVisibility requestedVisibility;
    private float nextRequestTime;

    private void OnEnable()
    {
        ResolveReferences();
        requestedVisibility = XrBoundaryVisibility.VisibilityNotSuppressed;
        nextRequestTime = 0f;
    }

    private void OnDisable()
    {
        if (feature != null)
            feature.TryRequestBoundaryVisibility(XrBoundaryVisibility.VisibilityNotSuppressed);
    }

    private void Update()
    {
        ResolveReferences();
        if (feature == null)
            return;

        bool passthroughTracking = cameraManager != null &&
                                   cameraManager.isActiveAndEnabled &&
                                   cameraManager.subsystem != null &&
                                   cameraManager.subsystem.running &&
                                   ARSession.state == ARSessionState.SessionTracking;
        XrBoundaryVisibility desired = passthroughTracking
            ? XrBoundaryVisibility.VisibilitySuppressed
            : XrBoundaryVisibility.VisibilityNotSuppressed;

        if (desired != requestedVisibility)
        {
            requestedVisibility = desired;
            nextRequestTime = 0f;
        }

        if (feature.currentVisibility == desired || Time.unscaledTime < nextRequestTime)
            return;

        feature.TryRequestBoundaryVisibility(desired);
        nextRequestTime = Time.unscaledTime + retryInterval;
    }

    private void ResolveReferences()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>();
        if (feature == null && OpenXRSettings.Instance != null)
            feature = OpenXRSettings.Instance.GetFeature<BoundaryVisibilityFeature>();
    }
}
