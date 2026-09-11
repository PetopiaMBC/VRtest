using UnityEngine;

public static class XRInteractionRuntimeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (Object.FindFirstObjectByType<XRPlayerInputStatePublisher>() != null)
            return;

        GameObject runtime = new GameObject("XR Interaction Runtime");
        Object.DontDestroyOnLoad(runtime);
        runtime.AddComponent<XRPlayerInputStatePublisher>();
        runtime.AddComponent<XRControllerContactInteractor>();
    }
}
