using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A self-contained world-space heart burst. The pink heart sprite is generated
/// once at runtime, so the effect does not depend on an external art asset.
/// </summary>
public sealed class PetHeartEffect : MonoBehaviour
{
    private sealed class FloatingHeart
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public float age;
        public float lifetime;
        public float baseScale;
        public float phase;
    }

    private static Sprite heartSprite;
    private static readonly Color HeartColor = new Color(1f, 0.26f, 0.58f, 1f);

    private readonly List<FloatingHeart> hearts = new List<FloatingHeart>();
    private Transform faceAnchor;
    private Camera viewingCamera;
    private int spawnCount;
    private float spawnDuration;
    private float effectLifetime;
    private int spawnedCount;
    private float elapsed;

    public static void Play(Transform anchor, Camera camera, int minimumCount, int maximumCount)
    {
        if (anchor == null)
            return;

        GameObject root = new GameObject("Petting Hearts (Runtime)");
        PetHeartEffect effect = root.AddComponent<PetHeartEffect>();
        effect.faceAnchor = anchor;
        effect.viewingCamera = camera;
        effect.spawnCount = Random.Range(
            Mathf.Max(1, minimumCount),
            Mathf.Max(minimumCount + 1, maximumCount + 1));
        effect.spawnDuration = 1.6f;
        effect.effectLifetime = 3.3f;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        int desiredSpawnCount = spawnDuration <= 0f
            ? spawnCount
            : Mathf.Min(spawnCount, Mathf.FloorToInt((elapsed / spawnDuration) * spawnCount) + 1);

        while (spawnedCount < desiredSpawnCount)
        {
            SpawnHeart(spawnedCount);
            spawnedCount++;
        }

        for (int i = hearts.Count - 1; i >= 0; i--)
        {
            FloatingHeart heart = hearts[i];
            heart.age += Time.deltaTime;
            float progress = Mathf.Clamp01(heart.age / heart.lifetime);
            heart.transform.position += heart.velocity * Time.deltaTime;
            heart.transform.position += Vector3.right *
                                        (Mathf.Sin(heart.phase + heart.age * 4.5f) *
                                         0.035f * Time.deltaTime);

            Camera camera = viewingCamera != null ? viewingCamera : Camera.main;
            if (camera != null)
                heart.transform.rotation = camera.transform.rotation;

            float appear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.16f, progress));
            float disappear = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, progress));
            float alpha = appear * disappear;
            heart.renderer.color = new Color(HeartColor.r, HeartColor.g, HeartColor.b, alpha);
            float pulse = 1f + Mathf.Sin(heart.age * 7f + heart.phase) * 0.08f;
            heart.transform.localScale = Vector3.one * (heart.baseScale * pulse);

            if (progress < 1f)
                continue;

            Destroy(heart.transform.gameObject);
            hearts.RemoveAt(i);
        }

        if (spawnedCount >= spawnCount && hearts.Count == 0 && elapsed >= effectLifetime)
            Destroy(gameObject);
    }

    private void SpawnHeart(int index)
    {
        if (faceAnchor == null)
            return;

        GameObject heartObject = new GameObject("Pink Heart " + (index + 1));
        heartObject.transform.SetParent(transform, true);
        Camera camera = viewingCamera != null ? viewingCamera : Camera.main;
        Vector3 horizontal = camera != null ? camera.transform.right : faceAnchor.root.right;
        Vector3 towardViewer = camera != null
            ? (camera.transform.position - faceAnchor.position).normalized
            : -faceAnchor.forward;
        heartObject.transform.position = faceAnchor.position +
                                         horizontal * Random.Range(-0.28f, 0.28f) +
                                         Vector3.up * Random.Range(-0.05f, 0.2f) +
                                         towardViewer * Random.Range(0.08f, 0.16f);

        SpriteRenderer renderer = heartObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetHeartSprite();
        renderer.color = new Color(HeartColor.r, HeartColor.g, HeartColor.b, 0f);
        renderer.sortingOrder = 100;

        float scale = Random.Range(0.09f, 0.15f);
        heartObject.transform.localScale = Vector3.one * scale;
        hearts.Add(new FloatingHeart
        {
            transform = heartObject.transform,
            renderer = renderer,
            velocity = Vector3.up * Random.Range(0.22f, 0.36f) +
                       horizontal * Random.Range(-0.055f, 0.055f),
            lifetime = Random.Range(1.35f, 2.05f),
            baseScale = scale,
            phase = Random.Range(0f, Mathf.PI * 2f)
        });
    }

    private static Sprite GetHeartSprite()
    {
        if (heartSprite != null)
            return heartSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Pink Heart",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normalizedX = ((x + 0.5f) / size) * 2.5f - 1.25f;
                float normalizedY = ((y + 0.5f) / size) * 2.5f - 1.1f;
                float a = normalizedX * normalizedX + normalizedY * normalizedY - 1f;
                float heartEquation = a * a * a -
                                      normalizedX * normalizedX *
                                      normalizedY * normalizedY * normalizedY;
                float alpha = Mathf.Clamp01(-heartEquation * 18f + 0.55f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        heartSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
        heartSprite.name = "Runtime Pink Heart Sprite";
        heartSprite.hideFlags = HideFlags.HideAndDontSave;
        return heartSprite;
    }
}
