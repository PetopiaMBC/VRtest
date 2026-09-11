using System.Collections;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>Requests the two Android permissions needed by the Q3 MR path.</summary>
[DisallowMultipleComponent]
public sealed class QuestMRPermissionGate : MonoBehaviour
{
    private const string ScenePermission = "com.oculus.permission.USE_SCENE";

    public bool CameraGranted { get; private set; }
    public bool SceneGranted { get; private set; }

    public IEnumerator EnsurePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        yield return RequestPermission(Permission.Camera, value => CameraGranted = value);
        yield return RequestPermission(ScenePermission, value => SceneGranted = value);

        if (!CameraGranted || !SceneGranted)
        {
            Debug.LogWarning(
                $"[Quest MR] Permission denied. CAMERA={CameraGranted}, USE_SCENE={SceneGranted}. " +
                "Passthrough or room setup may be unavailable until enabled in Quest settings.");
        }
#else
        CameraGranted = true;
        SceneGranted = true;
        yield break;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static IEnumerator RequestPermission(string permissionName, System.Action<bool> complete)
    {
        if (Permission.HasUserAuthorizedPermission(permissionName))
        {
            complete(true);
            yield break;
        }

        bool finished = false;
        bool granted = false;
        PermissionCallbacks callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ =>
        {
            granted = true;
            finished = true;
        };
        callbacks.PermissionDenied += _ => finished = true;
        callbacks.PermissionDeniedAndDontAskAgain += _ => finished = true;
        Permission.RequestUserPermission(permissionName, callbacks);

        while (!finished)
            yield return null;

        complete(granted);
    }
#endif
}
