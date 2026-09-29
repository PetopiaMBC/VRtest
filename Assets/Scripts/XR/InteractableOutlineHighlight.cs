using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// XR ????? ?? ???? ?? ????? ? ?? 3D ??????
/// ??? ??? ?? ?????. ?? ????? ?? ??? ???? ????.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractableOutlineHighlight : MonoBehaviour
{
    private const string OutlineMaterialResourcePath = "XRInteractionOutline";
    private const string OutlineShaderName = "Hidden/VRTest/InteractionOutline";
    private const string OutlineObjectSuffix = " (Interaction Outline)";

    private sealed class OutlineRenderer
    {
        public Renderer Source;
        public Renderer Shell;
    }

    private readonly List<OutlineRenderer> outlineRenderers = new List<OutlineRenderer>();
    private Material outlineMaterial;
    private bool highlighted;
    private bool missingShaderLogged;

    public bool IsHighlighted => highlighted;

    /// <summary>??? ??? ???? ??? ?? ??? ?????.</summary>
    public void SetHighlighted(bool visible, Color color, float width)
    {
        highlighted = visible;
        if (!visible)
        {
            SetShellsActive(false);
            return;
        }

        EnsureShellsCreated();
        if (outlineMaterial == null)
            return;

        outlineMaterial.SetColor("_OutlineColor", color);
        outlineMaterial.SetFloat("_OutlineWidth", Mathf.Max(0.0005f, width));
        SetShellsActive(true);
        SynchronizeRendererStates();
    }

    private void LateUpdate()
    {
        if (highlighted)
            SynchronizeRendererStates();
    }

    private void EnsureShellsCreated()
    {
        if (outlineMaterial != null)
            return;

        Material template = Resources.Load<Material>(OutlineMaterialResourcePath);
        Shader shader = template != null ? template.shader : Shader.Find(OutlineShaderName);
        if (shader == null || template == null)
        {
            if (!missingShaderLogged)
            {
                Debug.LogWarning(
                    $"Interaction outline material Resources/{OutlineMaterialResourcePath}.mat " +
                    $"or shader '{OutlineShaderName}' was not found.",
                    this);
                missingShaderLogged = true;
            }
            return;
        }

        // The Resources material keeps the hidden shader in Android builds.
        // A runtime copy lets each interactable use its own color and width.
        outlineMaterial = new Material(template)
        {
            name = $"{name} Interaction Outline (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };

        Renderer[] sources = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            Renderer source = sources[i];
            if (source == null || source is ParticleSystemRenderer ||
                source is TrailRenderer || source is LineRenderer ||
                source.gameObject.name.EndsWith(OutlineObjectSuffix))
                continue;

            Renderer shell = CreateOutlineShell(source);
            if (shell == null)
                continue;

            outlineRenderers.Add(new OutlineRenderer
            {
                Source = source,
                Shell = shell
            });
        }
    }

    private Renderer CreateOutlineShell(Renderer source)
    {
        GameObject shellObject = new GameObject(source.gameObject.name + OutlineObjectSuffix)
        {
            layer = source.gameObject.layer,
            hideFlags = HideFlags.HideAndDontSave
        };
        shellObject.transform.SetParent(source.transform, false);
        shellObject.transform.localPosition = Vector3.zero;
        shellObject.transform.localRotation = Quaternion.identity;
        shellObject.transform.localScale = Vector3.one;

        Renderer shell;
        int materialCount;
        if (source is SkinnedMeshRenderer sourceSkinned)
        {
            if (sourceSkinned.sharedMesh == null)
            {
                DestroyRuntimeObject(shellObject);
                return null;
            }

            SkinnedMeshRenderer shellSkinned =
                shellObject.AddComponent<SkinnedMeshRenderer>();
            shellSkinned.sharedMesh = sourceSkinned.sharedMesh;
            shellSkinned.bones = sourceSkinned.bones;
            shellSkinned.rootBone = sourceSkinned.rootBone;
            shellSkinned.localBounds = sourceSkinned.localBounds;
            shellSkinned.quality = sourceSkinned.quality;
            shellSkinned.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
            shell = shellSkinned;
            materialCount = Mathf.Max(1, sourceSkinned.sharedMesh.subMeshCount);
        }
        else if (source is MeshRenderer &&
                 source.TryGetComponent(out MeshFilter sourceFilter) &&
                 sourceFilter.sharedMesh != null)
        {
            MeshFilter shellFilter = shellObject.AddComponent<MeshFilter>();
            shellFilter.sharedMesh = sourceFilter.sharedMesh;
            shell = shellObject.AddComponent<MeshRenderer>();
            materialCount = Mathf.Max(1, sourceFilter.sharedMesh.subMeshCount);
        }
        else
        {
            DestroyRuntimeObject(shellObject);
            return null;
        }

        Material[] materials = new Material[materialCount];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = outlineMaterial;
        shell.sharedMaterials = materials;
        shell.shadowCastingMode = ShadowCastingMode.Off;
        shell.receiveShadows = false;
        shell.lightProbeUsage = LightProbeUsage.Off;
        shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
        shell.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        shell.renderingLayerMask = source.renderingLayerMask;
        shell.enabled = false;
        return shell;
    }

    private void SynchronizeRendererStates()
    {
        for (int i = outlineRenderers.Count - 1; i >= 0; i--)
        {
            OutlineRenderer entry = outlineRenderers[i];
            if (entry.Source == null || entry.Shell == null)
            {
                outlineRenderers.RemoveAt(i);
                continue;
            }

            entry.Shell.enabled = highlighted && entry.Source.enabled;
        }
    }

    private void SetShellsActive(bool active)
    {
        for (int i = 0; i < outlineRenderers.Count; i++)
        {
            Renderer shell = outlineRenderers[i].Shell;
            if (shell != null)
                shell.enabled = active && outlineRenderers[i].Source != null &&
                                outlineRenderers[i].Source.enabled;
        }
    }

    private void OnDisable()
    {
        highlighted = false;
        SetShellsActive(false);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < outlineRenderers.Count; i++)
        {
            Renderer shell = outlineRenderers[i].Shell;
            if (shell != null)
                DestroyRuntimeObject(shell.gameObject);
        }
        outlineRenderers.Clear();

        if (outlineMaterial != null)
            DestroyRuntimeObject(outlineMaterial);
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
