using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shows a world-space pet prompt above the dog and accepts E only when the
/// player is close enough and the camera is actually aimed at the dog.
/// </summary>
public sealed class DogPetInteraction : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField, Min(0.1f)] private float interactionDistance = 1.3f;
    [SerializeField, Range(1f, 90f)] private float lookAngle = 38f;
    [SerializeField, Min(0.05f)] private float promptHeight = 0.72f;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Camera playerCamera;

    [Header("Dog pose")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform[] eyeObjects;
    [SerializeField] private Animator animator;
    [SerializeField] private string petTrigger = "Pet";
    [SerializeField, Range(2, 3)] private int headTilts = 3;
    [SerializeField, Range(2f, 20f)] private float tiltAngle = 9f;
    [SerializeField, Min(0.05f)] private float tiltDuration = 0.22f;
    [SerializeField, Min(0.5f)] private float petFocusDuration = 2f;

    [Header("Prompt")]
    [SerializeField] private string promptText = "E  Pet";
    [SerializeField] private Color promptColor = new Color(1f, 0.86f, 0.35f, 1f);

    private Canvas promptCanvas;
    private Canvas heartCanvas;
    private GameObject promptBackground;
    private Text promptLabel;
    private readonly List<Heart> hearts = new List<Heart>();
    private Quaternion headRestRotation;
    private bool headRestRotationCaptured;
    private bool canPet;
    private bool petting;
    private PrototypePetAgent petAgent;
    private Transform promptFollowRoot;
    private Vector3 fixedPromptLocalPosition;
    private bool fixedPromptPositionCaptured;
    private Font runtimeFont;

    private sealed class Heart
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public Vector2 StartPosition;
        public float StartTime;
        public float Duration;
        public float Drift;
    }

    private static readonly string[] HeadNames =
    {
        "Head", "head", "Face", "face", "Neck", "neck"
    };

    private static readonly string[] EyeNames =
    {
        "Eye", "eye", "Eyes", "eyes", "Eye_L", "Eye_R",
        "LeftEye", "RightEye", "Left Eye", "Right Eye"
    };

    private static readonly string[] PlayerNames =
    {
        "Player", "player", "Character", "character", "MainCharacter"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnNamedDogs()
    {
        InstallOnNamedDogsInLoadedScenes();

        GameObject installerObject = new GameObject("PetInteractionInstaller");
        DontDestroyOnLoad(installerObject);
        installerObject.AddComponent<DogPetInteractionInstaller>();
    }

    private static void InstallOnNamedDogsInLoadedScenes()
    {
        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            if (!IsDogLikeName(objects[i].name) || HasDogNamedAncestor(objects[i].transform))
                continue;

            if (objects[i].GetComponent<DogPetInteraction>() == null)
                objects[i].AddComponent<DogPetInteraction>();
        }
    }

    private sealed class DogPetInteractionInstaller : MonoBehaviour
    {
        private float nextScanTime;

        private void Update()
        {
            if (Time.unscaledTime < nextScanTime)
                return;

            nextScanTime = Time.unscaledTime + 0.5f;
            InstallOnNamedDogsInLoadedScenes();
        }
    }

    private static bool HasDogNamedAncestor(Transform candidate)
    {
        Transform ancestor = candidate.parent;
        while (ancestor != null)
        {
            if (IsDogLikeName(ancestor.name))
                return true;
            ancestor = ancestor.parent;
        }

        return false;
    }

    private static bool IsDogLikeName(string objectName)
    {
        string loweredName = objectName.ToLowerInvariant();
        if (loweredName.Contains("movement root"))
            return false;

        return loweredName.Contains("dog") || loweredName.Contains("puppy");
    }

    private void Awake()
    {
        ResolveReferences();
        CreatePrompt();
    }

    private void OnDestroy()
    {
        if (promptCanvas != null)
            Destroy(promptCanvas.gameObject);
        if (heartCanvas != null)
            Destroy(heartCanvas.gameObject);
    }

    private void Update()
    {
        ResolveReferences();

        canPet = IsPlayerCloseAndLooking();
        bool promptVisible = canPet && !petting;
        SetPromptVisible(promptVisible);
        SetDogInteractionFocus(petting || promptVisible);

        if (promptVisible && IsPetPressed())
            StartCoroutine(PetRoutine());

        UpdateHearts();
    }

    private void LateUpdate()
    {
        if (promptCanvas == null || playerCamera == null)
            return;

        promptCanvas.transform.position = GetPromptWorldPosition();
        promptCanvas.transform.LookAt(playerCamera.transform);
        promptCanvas.transform.Rotate(0f, 180f, 0f);

        if (heartCanvas != null)
        {
            heartCanvas.transform.position = GetHeartWorldPosition();
            heartCanvas.transform.LookAt(playerCamera.transform);
            heartCanvas.transform.Rotate(0f, 180f, 0f);
        }
    }

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (playerTransform == null)
            playerTransform = ResolvePlayer();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (petAgent == null)
            petAgent = GetComponent<PrototypePetAgent>();

        if (headTransform == null)
            headTransform = FindChildByNames(HeadNames);

        if (headTransform != null && !headRestRotationCaptured)
        {
            headRestRotation = headTransform.localRotation;
            headRestRotationCaptured = true;
        }

        if (eyeObjects == null || eyeObjects.Length == 0)
            eyeObjects = FindEyes();

        ResolvePromptFollowRoot();

        if (promptCanvas != null)
        {
            promptCanvas.worldCamera = playerCamera;
            promptCanvas.transform.position = GetPromptWorldPosition();
        }

        if (heartCanvas != null)
        {
            heartCanvas.worldCamera = playerCamera;
            heartCanvas.transform.position = GetHeartWorldPosition();
        }
    }

    private void SetDogInteractionFocus(bool focused)
    {
        if (petAgent == null)
            petAgent = GetComponent<PrototypePetAgent>();

        if (petAgent != null)
            petAgent.SetInteractionFocus(focused);
    }

    private void ResolvePromptFollowRoot()
    {
        Transform desiredRoot = petAgent != null && petAgent.MovementRoot != null
            ? petAgent.MovementRoot
            : (transform.parent != null ? transform.parent : transform);

        if (promptFollowRoot != desiredRoot)
        {
            promptFollowRoot = desiredRoot;
            fixedPromptPositionCaptured = false;
        }

        if (fixedPromptPositionCaptured || promptFollowRoot == null)
            return;

        Vector3 anchorWorldPosition = headTransform != null
            ? headTransform.position
            : GetDogBounds().max;
        fixedPromptLocalPosition = promptFollowRoot.InverseTransformPoint(anchorWorldPosition);
        fixedPromptLocalPosition += Vector3.up * Mathf.Clamp(promptHeight, 0.05f, 1f);
        fixedPromptPositionCaptured = true;
    }

    private Transform ResolvePlayer()
    {
        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
                return taggedPlayer.transform;
        }
        catch (UnityException)
        {
            // The project may not define a Player tag.
        }

        for (int i = 0; i < PlayerNames.Length; i++)
        {
            GameObject namedPlayer = GameObject.Find(PlayerNames[i]);
            if (namedPlayer != null)
                return namedPlayer.transform;
        }

        return playerCamera != null ? playerCamera.transform : null;
    }

    private Transform FindChildByNames(string[] names)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == transform)
                continue;

            for (int j = 0; j < names.Length; j++)
            {
                if (children[i].name.Equals(names[j], System.StringComparison.OrdinalIgnoreCase))
                    return children[i];
            }
        }

        return null;
    }

    private Transform[] FindEyes()
    {
        List<Transform> foundEyes = new List<Transform>();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == transform)
                continue;

            string childName = children[i].name.ToLowerInvariant();
            for (int j = 0; j < EyeNames.Length; j++)
            {
                if (childName == EyeNames[j].ToLowerInvariant() ||
                    childName.Contains(EyeNames[j].ToLowerInvariant()))
                {
                    if (!foundEyes.Contains(children[i]))
                        foundEyes.Add(children[i]);
                    break;
                }
            }
        }

        return foundEyes.ToArray();
    }

    private Vector3 GetHeartWorldPosition()
    {
        Transform faceTransform = FindChildByNames(new[] { "Head", "head", "Face", "face" });
        if (faceTransform != null)
            return faceTransform.position + Vector3.up * 0.08f;

        if (headTransform != null)
            return headTransform.position + Vector3.up * 0.08f;

        return GetDogBounds().center + Vector3.up * 0.1f;
    }

    private bool IsPlayerCloseAndLooking()
    {
        if (playerCamera == null)
            return false;

        Bounds dogBounds = GetDogBounds();
        Vector3 playerPosition = playerTransform != null
            ? playerTransform.position
            : playerCamera.transform.position;
        Vector3 closestPoint = dogBounds.ClosestPoint(playerPosition);
        if ((closestPoint - playerPosition).sqrMagnitude > interactionDistance * interactionDistance)
            return false;

        Vector3 targetPosition = GetGazeTargetPosition(dogBounds);
        Vector3 cameraToDog = targetPosition - playerCamera.transform.position;
        if (cameraToDog.sqrMagnitude < 0.01f ||
            Vector3.Angle(playerCamera.transform.forward, cameraToDog) > lookAngle)
            return false;

        // The view ray must pass through the dog's rendered bounds. This works
        // even when the imported model has no collider of its own.
        Ray gazeRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!dogBounds.IntersectRay(gazeRay, out float boundsDistance))
            return false;

        // Reject a wall or another object that blocks the dog before its bounds.
        if (Physics.Raycast(gazeRay, out RaycastHit hit, boundsDistance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            hit.distance < boundsDistance - 0.03f &&
            !IsDogPart(hit.transform))
        {
            return false;
        }

        return true;
    }

    private Bounds GetDogBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return bounds;
    }

    private Vector3 GetGazeTargetPosition(Bounds dogBounds)
    {
        if (headTransform != null)
            return headTransform.position;

        return dogBounds.center + Vector3.up * Mathf.Min(0.15f, dogBounds.extents.y * 0.2f);
    }

    private Vector3 GetPromptWorldPosition()
    {
        if (fixedPromptPositionCaptured && promptFollowRoot != null)
            return promptFollowRoot.TransformPoint(fixedPromptLocalPosition);

        Bounds dogBounds = GetDogBounds();
        return dogBounds.max + Vector3.up * Mathf.Clamp(promptHeight, 0.05f, 1f);
    }

    private bool IsDogPart(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform);
    }

    private bool IsPetPressed()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private IEnumerator PetRoutine()
    {
        petting = true;
        SetDogInteractionFocus(true);
        SetPromptVisible(false);
        SpawnHearts();
        float focusEndTime = Time.unscaledTime + petFocusDuration;

        if (animator != null && !string.IsNullOrWhiteSpace(petTrigger))
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == petTrigger && parameters[i].type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(petTrigger);
                    break;
                }
            }
        }

        bool[] eyeStates = new bool[eyeObjects.Length];
        for (int i = 0; i < eyeObjects.Length; i++)
        {
            if (eyeObjects[i] == null)
                continue;

            eyeStates[i] = eyeObjects[i].gameObject.activeSelf;
            eyeObjects[i].gameObject.SetActive(false);
        }

        if (headTransform != null)
        {
            for (int i = 0; i < headTilts; i++)
            {
                float direction = i % 2 == 0 ? 1f : -1f;
                yield return RotateHeadTo(headRestRotation * Quaternion.Euler(0f, 0f, direction * tiltAngle));
                yield return RotateHeadTo(headRestRotation);
            }
        }
        else
        {
            yield return new WaitForSeconds(tiltDuration * headTilts * 2f);
        }

        float remainingFocusTime = focusEndTime - Time.unscaledTime;
        if (remainingFocusTime > 0f)
            yield return new WaitForSecondsRealtime(remainingFocusTime);

        for (int i = 0; i < eyeObjects.Length; i++)
        {
            if (eyeObjects[i] != null)
                eyeObjects[i].gameObject.SetActive(eyeStates[i]);
        }

        petting = false;
        SetDogInteractionFocus(false);
    }

    private IEnumerator RotateHeadTo(Quaternion target)
    {
        Quaternion start = headTransform.localRotation;
        float elapsed = 0f;
        while (elapsed < tiltDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / tiltDuration);
            headTransform.localRotation = Quaternion.Slerp(start, target, t);
            yield return null;
        }

        headTransform.localRotation = target;
    }

    private void CreatePrompt()
    {
        runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("DogPetPrompt");
        canvasObject.transform.SetParent(transform, false);
        promptCanvas = canvasObject.AddComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.WorldSpace;
        promptCanvas.worldCamera = playerCamera;
        promptCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(230f, 52f);
        canvasRect.localScale = Vector3.one * 0.004f;

        promptBackground = new GameObject("PromptBackground");
        promptBackground.transform.SetParent(canvasObject.transform, false);
        Image panel = promptBackground.AddComponent<Image>();
        panel.color = new Color(0.06f, 0.04f, 0.08f, 0.88f);
        RectTransform panelRect = promptBackground.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject labelObject = new GameObject("PromptLabel");
        labelObject.transform.SetParent(canvasObject.transform, false);
        promptLabel = labelObject.AddComponent<Text>();
        promptLabel.font = runtimeFont;
        promptLabel.fontSize = 23;
        promptLabel.fontStyle = FontStyle.Bold;
        promptLabel.alignment = TextAnchor.MiddleCenter;
        promptLabel.text = promptText;
        promptLabel.color = promptColor;
        promptLabel.raycastTarget = false;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        SetPromptVisible(false);

        GameObject heartCanvasObject = new GameObject("DogPetHearts");
        heartCanvasObject.transform.SetParent(transform, false);
        heartCanvas = heartCanvasObject.AddComponent<Canvas>();
        heartCanvas.renderMode = RenderMode.WorldSpace;
        heartCanvas.worldCamera = playerCamera;
        heartCanvas.sortingOrder = 101;

        CanvasScaler heartScaler = heartCanvasObject.AddComponent<CanvasScaler>();
        heartScaler.dynamicPixelsPerUnit = 10f;

        RectTransform heartCanvasRect = heartCanvasObject.GetComponent<RectTransform>();
        heartCanvasRect.sizeDelta = new Vector2(260f, 160f);
        heartCanvasRect.localScale = Vector3.one * 0.006f;
        heartCanvasObject.transform.position = GetHeartWorldPosition();
        heartCanvasObject.transform.LookAt(playerCamera != null ? playerCamera.transform : transform);
        heartCanvasObject.transform.Rotate(0f, 180f, 0f);
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptBackground != null)
            promptBackground.SetActive(visible);
        if (promptLabel != null)
            promptLabel.gameObject.SetActive(visible);
    }

    private void SpawnHearts()
    {
        if (heartCanvas == null)
            return;

        for (int i = 0; i < 4; i++)
        {
            GameObject heartObject = new GameObject("PetHeart");
            heartObject.transform.SetParent(heartCanvas.transform, false);

            Text heartLabel = heartObject.AddComponent<Text>();
            heartLabel.font = runtimeFont;
            heartLabel.fontSize = Random.Range(24, 34);
            heartLabel.alignment = TextAnchor.MiddleCenter;
            heartLabel.text = "♥";
            heartLabel.color = new Color(1f, Random.Range(0.35f, 0.7f), 0.72f, 1f);
            heartLabel.raycastTarget = false;

            RectTransform heartRect = heartObject.GetComponent<RectTransform>();
            heartRect.sizeDelta = new Vector2(60f, 60f);
            heartRect.anchoredPosition = new Vector2(Random.Range(-75f, 75f), Random.Range(-35f, 35f));

            CanvasGroup group = heartObject.AddComponent<CanvasGroup>();
            hearts.Add(new Heart
            {
                Rect = heartRect,
                Group = group,
                StartPosition = heartRect.anchoredPosition,
                StartTime = Time.time,
                Duration = Random.Range(0.65f, 1.05f),
                Drift = Random.Range(-35f, 35f)
            });
        }
    }

    private void UpdateHearts()
    {
        for (int i = hearts.Count - 1; i >= 0; i--)
        {
            Heart heart = hearts[i];
            float t = (Time.time - heart.StartTime) / heart.Duration;
            if (t >= 1f || heart.Rect == null)
            {
                if (heart.Rect != null)
                    Destroy(heart.Rect.gameObject);
                hearts.RemoveAt(i);
                continue;
            }

            heart.Rect.anchoredPosition = heart.StartPosition + new Vector2(heart.Drift * t, 55f * t);
            heart.Rect.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.15f, Mathf.Sin(t * Mathf.PI));
            heart.Group.alpha = Mathf.Sin((1f - t) * Mathf.PI * 0.5f);
        }
    }
}
