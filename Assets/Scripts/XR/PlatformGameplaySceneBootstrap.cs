using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Thin Q2/Q3 scene layer. The existing PlayScene stays the single shared GameplayCore.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlatformGameplaySceneBootstrap : MonoBehaviour
{
    [SerializeField] private PlayerDeviceMode deviceMode = PlayerDeviceMode.Quest2VR;
    [SerializeField] private string sharedGameplayScene = "PlayScene";
    [SerializeField] private Vector2 q2PlayableSize = new Vector2(9.1f, 9.1f);

    public void Configure(PlayerDeviceMode mode)
    {
        deviceMode = mode;
    }

    private IEnumerator Start()
    {
        Scene shared = SceneManager.GetSceneByName(sharedGameplayScene);
        if (!shared.isLoaded)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sharedGameplayScene,
                LoadSceneMode.Additive);
            if (operation == null)
                yield break;
            while (!operation.isDone)
                yield return null;
        }

        yield return null;

        if (deviceMode == PlayerDeviceMode.Quest3MR)
        {
            QuestMRPermissionGate permissionGate = GetComponent<QuestMRPermissionGate>();
            if (permissionGate == null)
                permissionGate = gameObject.AddComponent<QuestMRPermissionGate>();
            yield return permissionGate.EnsurePermissions();
        }

        ConfigureSharedGameplay();
    }

    private void ConfigureSharedGameplay()
    {
        ShepherdCompanionAI companion = FindFirstObjectByType<ShepherdCompanionAI>();
        FetchBallInteraction ball = FindFirstObjectByType<FetchBallInteraction>();
        ConfigurePassthroughCamera(deviceMode == PlayerDeviceMode.Quest3MR);

        if (deviceMode == PlayerDeviceMode.Quest3MR)
        {
            SetVirtualRoomVisible(false);
            if (MRUK.Instance == null)
                gameObject.AddComponent<MRUK>();
            if (GetComponent<Q3MRBoundaryVisibilityController>() == null)
                gameObject.AddComponent<Q3MRBoundaryVisibilityController>();
            SceneNavigation navigation = GetComponent<SceneNavigation>();
            if (navigation == null)
                navigation = gameObject.AddComponent<SceneNavigation>();
            Q3NavMeshPetSpaceProvider provider = GetComponent<Q3NavMeshPetSpaceProvider>();
            if (provider == null)
                provider = gameObject.AddComponent<Q3NavMeshPetSpaceProvider>();
            ConfigureBinder(provider, companion, ball);
            return;
        }

        SetVirtualRoomVisible(true);
        BoxCollider playArea = CreateQ2PlayArea();
        Transform spawn = CreatePoint("DogSpawnPosition", companion != null ? companion.transform.position : Vector3.zero);
        Transform rest = CreatePoint("DogRestPosition", spawn.position + Vector3.forward * 2f);
        Transform recovery = CreatePoint("DogRecoveryPosition", playArea.bounds.center);

        Q2VirtualRoomPetSpaceProvider q2Provider = GetComponent<Q2VirtualRoomPetSpaceProvider>();
        if (q2Provider == null)
            q2Provider = gameObject.AddComponent<Q2VirtualRoomPetSpaceProvider>();
        q2Provider.Configure(spawn, rest, recovery, playArea);
        ConfigureBinder(q2Provider, companion, ball);

        Q2VirtualRoomLocomotion locomotion = GetComponent<Q2VirtualRoomLocomotion>();
        if (locomotion == null)
            locomotion = gameObject.AddComponent<Q2VirtualRoomLocomotion>();
        Camera camera = Camera.main;
        locomotion.Configure(
            camera != null ? camera.transform.root : null,
            camera != null ? camera.transform : null,
            playArea);
    }

    private void ConfigureBinder(
        MonoBehaviour provider,
        ShepherdCompanionAI companion,
        FetchBallInteraction ball)
    {
        PetSpaceContractBinder binder = GetComponent<PetSpaceContractBinder>();
        if (binder == null)
            binder = gameObject.AddComponent<PetSpaceContractBinder>();
        binder.Configure(provider, companion, ball);
    }

    private BoxCollider CreateQ2PlayArea()
    {
        Transform existing = transform.Find("PlayerMoveArea");
        GameObject area = existing != null ? existing.gameObject : new GameObject("PlayerMoveArea");
        area.transform.SetParent(transform, false);
        area.transform.position = Vector3.zero;
        BoxCollider collider = area.GetComponent<BoxCollider>();
        if (collider == null)
            collider = area.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(q2PlayableSize.x, 0.1f, q2PlayableSize.y);
        return collider;
    }

    private Transform CreatePoint(string pointName, Vector3 position)
    {
        Transform point = transform.Find(pointName);
        if (point == null)
        {
            point = new GameObject(pointName).transform;
            point.SetParent(transform, false);
        }
        point.position = position;
        return point;
    }

    private static void SetVirtualRoomVisible(bool visible)
    {
        GameObject room = GameObject.Find("Play Room");
        if (room != null)
            room.SetActive(visible);
    }

    private static void ConfigurePassthroughCamera(bool enabled)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        ARCameraManager cameraManager = camera.GetComponent<ARCameraManager>();
        if (cameraManager == null)
            cameraManager = camera.gameObject.AddComponent<ARCameraManager>();

        cameraManager.enabled = enabled;
        camera.clearFlags = enabled ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
        if (enabled)
            camera.backgroundColor = Color.clear;
    }
}
