using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Whole-object grabbing and physical collision support for a non-rigged shepherd mesh.
/// Keyboard G grabbing is available for desktop testing, while XRGrabInteractable
/// selection is detected through reflection so this script stays independent of XRI.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShepherdStaticGrabInteraction : MonoBehaviour
{
    [Header("Keyboard grab")]
    [SerializeField] private bool useKeyboardGrab = true;
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0.5f)] private float grabDistance = 3f;
    [SerializeField, Min(0.5f)] private float holdDistance = 2.2f;
    [SerializeField] private float holdVerticalOffset = -0.2f;
    [SerializeField, Min(1f)] private float holdFollowSpeed = 12f;

    [Header("Physical collision")]
    [SerializeField, Min(0f)] private float colliderPadding = 0.04f;

    private ShepherdCompanionAI shepherdAI;
    private Rigidbody body;
    private BoxCollider physicalCollider;
    private Component xrGrabInteractable;
    private PropertyInfo xrIsSelectedProperty;

    private bool holding;
    private bool keyboardHolding;
    private bool xrWasSelected;
    private bool aiWasEnabled;
    private Vector3 holdVelocity;
    private Quaternion heldRotation;

    private void Awake()
    {
        ResolveReferences();
        EnsurePhysicalComponents();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePhysicalComponents();
    }

    private void Update()
    {
        ResolveReferences();

        bool xrSelected = IsXRSelected();
        if (xrSelected != xrWasSelected)
        {
            if (xrSelected)
                BeginHold(false);
            else if (holding && !keyboardHolding)
                EndHold();

            xrWasSelected = xrSelected;
        }

        if (!useKeyboardGrab || xrSelected || Keyboard.current == null ||
            !Keyboard.current.gKey.wasPressedThisFrame)
            return;

        if (holding && keyboardHolding)
            EndHold();
        else if (!holding && CanKeyboardGrab())
            BeginHold(true);
    }

    private void LateUpdate()
    {
        if (!holding || !keyboardHolding || playerCamera == null)
            return;

        Vector3 targetPosition =
            playerCamera.transform.position +
            playerCamera.transform.forward * holdDistance +
            playerCamera.transform.up * holdVerticalOffset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref holdVelocity,
            1f / holdFollowSpeed);

        transform.rotation = heldRotation;
    }

    private void OnDisable()
    {
        if (holding)
            EndHold();
    }

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (shepherdAI == null)
            shepherdAI = GetComponent<ShepherdCompanionAI>();

        if (body == null)
            body = GetComponent<Rigidbody>();

        if (physicalCollider == null)
            physicalCollider = GetComponent<BoxCollider>();

        if (xrGrabInteractable == null)
            ResolveXRGrabInteractable();
    }

    private void EnsurePhysicalComponents()
    {
        if (physicalCollider == null)
            physicalCollider = GetComponent<BoxCollider>();
        if (physicalCollider == null)
            physicalCollider = gameObject.AddComponent<BoxCollider>();

        ConfigureColliderFromRenderers();
        physicalCollider.isTrigger = false;
        physicalCollider.enabled = true;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();

        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotationX |
                           RigidbodyConstraints.FreezeRotationZ;
    }

    private void ConfigureColliderFromRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        physicalCollider.center = transform.InverseTransformPoint(bounds.center);

        Vector3 scale = transform.lossyScale;
        scale.x = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        scale.y = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        scale.z = Mathf.Max(Mathf.Abs(scale.z), 0.0001f);

        physicalCollider.size = new Vector3(
            bounds.size.x / scale.x + colliderPadding,
            bounds.size.y / scale.y + colliderPadding,
            bounds.size.z / scale.z + colliderPadding);
    }

    private bool CanKeyboardGrab()
    {
        if (playerCamera == null)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            grabDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                return true;
        }

        return false;
    }

    private void BeginHold(bool fromKeyboard)
    {
        if (holding)
            return;

        holding = true;
        keyboardHolding = fromKeyboard;
        holdVelocity = Vector3.zero;
        heldRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (shepherdAI != null)
        {
            aiWasEnabled = shepherdAI.enabled;
            shepherdAI.enabled = false;
        }

        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    private void EndHold()
    {
        if (!holding)
            return;

        holding = false;
        keyboardHolding = false;
        holdVelocity = Vector3.zero;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
        }

        if (shepherdAI != null)
            shepherdAI.enabled = aiWasEnabled;
    }

    private void ResolveXRGrabInteractable()
    {
        Type xrType = Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable, Unity.XR.Interaction.Toolkit");

        if (xrType == null)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && xrType == null; i++)
                xrType = assemblies[i].GetType(
                    "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable");
        }

        if (xrType == null)
            return;

        xrGrabInteractable = GetComponent(xrType);
        if (xrGrabInteractable != null)
            xrIsSelectedProperty = xrType.GetProperty(
                "isSelected",
                BindingFlags.Instance | BindingFlags.Public);
    }

    private bool IsXRSelected()
    {
        if (xrGrabInteractable == null || xrIsSelectedProperty == null)
            return false;

        object value = xrIsSelectedProperty.GetValue(xrGrabInteractable);
        return value is bool selected && selected;
    }
}
