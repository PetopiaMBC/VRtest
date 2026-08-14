using UnityEngine;

/// <summary>
/// Runtime-only prototype scene builder for the Meta Quest 3 pet demo.
/// It creates an open-top 8 x 8 room and a simple placeholder dog at play time.
/// </summary>
public sealed class PetRoomPrototype : MonoBehaviour
{
    [Header("Room")]
    [Min(2f)] public float roomSize = 8f;
    [Min(0.1f)] public float wallHeight = 2.5f;
    [Min(0.05f)] public float wallThickness = 0.15f;
    [Min(0.02f)] public float floorThickness = 0.1f;
    public Vector3 roomOffset = Vector3.zero;

    [Header("Dog")]
    public bool createDog = true;
    public Vector3 dogStartOffset = Vector3.zero;

    private Transform generatedRoot;

    private void Awake()
    {
        BuildPrototype();
    }

    private void BuildPrototype()
    {
        if (generatedRoot != null)
        {
            Destroy(generatedRoot.gameObject);
        }

        generatedRoot = new GameObject("Generated Pet Room").transform;
        generatedRoot.SetParent(transform, false);
        generatedRoot.position = roomOffset;

        Material floorMaterial = CreateMaterial(new Color(0.28f, 0.33f, 0.38f));
        Material wallMaterial = CreateMaterial(new Color(0.72f, 0.76f, 0.82f));

        CreateBox(
            "Floor",
            new Vector3(0f, -floorThickness * 0.5f, 0f),
            new Vector3(roomSize, floorThickness, roomSize),
            floorMaterial);

        float halfRoom = roomSize * 0.5f;
        float wallY = wallHeight * 0.5f;

        CreateBox(
            "Wall North",
            new Vector3(0f, wallY, halfRoom),
            new Vector3(roomSize, wallHeight, wallThickness),
            wallMaterial);
        CreateBox(
            "Wall South",
            new Vector3(0f, wallY, -halfRoom),
            new Vector3(roomSize, wallHeight, wallThickness),
            wallMaterial);
        CreateBox(
            "Wall East",
            new Vector3(halfRoom, wallY, 0f),
            new Vector3(wallThickness, wallHeight, roomSize),
            wallMaterial);
        CreateBox(
            "Wall West",
            new Vector3(-halfRoom, wallY, 0f),
            new Vector3(wallThickness, wallHeight, roomSize),
            wallMaterial);

        if (createDog)
        {
            CreatePlaceholderDog(halfRoom);
        }
    }

    private GameObject CreateBox(string objectName, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.SetParent(generatedRoot, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = localScale;

        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        return box;
    }

    private void CreatePlaceholderDog(float halfRoom)
    {
        GameObject dog = new GameObject("Prototype Dog");
        dog.transform.SetParent(generatedRoot, false);

        float safeMargin = Mathf.Max(0.7f, wallThickness + 0.5f);
        Vector3 startPosition = dogStartOffset;
        startPosition.x = Mathf.Clamp(startPosition.x, -halfRoom + safeMargin, halfRoom - safeMargin);
        startPosition.z = Mathf.Clamp(startPosition.z, -halfRoom + safeMargin, halfRoom - safeMargin);
        dog.transform.localPosition = startPosition;

        CharacterController controller = dog.AddComponent<CharacterController>();
        controller.height = 1.15f;
        controller.radius = 0.34f;
        controller.center = new Vector3(0f, 0.58f, 0f);
        controller.stepOffset = 0.2f;
        controller.skinWidth = 0.03f;

        PrototypePetAgent agent = dog.AddComponent<PrototypePetAgent>();
        agent.roomHalfExtent = halfRoom - safeMargin;

        Material bodyMaterial = CreateMaterial(new Color(0.62f, 0.35f, 0.16f));
        Material headMaterial = CreateMaterial(new Color(0.72f, 0.43f, 0.22f));
        Material noseMaterial = CreateMaterial(new Color(0.04f, 0.025f, 0.02f));
        Material eyeWhiteMaterial = CreateMaterial(new Color(0.95f, 0.95f, 0.9f));

        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.58f, 0f), new Vector3(0.72f, 0.58f, 0.9f), Vector3.zero, bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.15f, 0.16f), Vector3.one * 0.55f, Vector3.zero, headMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Nose", new Vector3(0f, 1.1f, 0.43f), Vector3.one * 0.16f, Vector3.zero, noseMaterial);

        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Ear Left", new Vector3(-0.22f, 1.35f, 0.08f), new Vector3(0.18f, 0.3f, 0.14f), new Vector3(0f, 0f, -28f), bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Ear Right", new Vector3(0.22f, 1.35f, 0.08f), new Vector3(0.18f, 0.3f, 0.14f), new Vector3(0f, 0f, 28f), bodyMaterial);

        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Eye White Left", new Vector3(-0.16f, 1.22f, 0.39f), Vector3.one * 0.12f, Vector3.zero, eyeWhiteMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Eye White Right", new Vector3(0.16f, 1.22f, 0.39f), Vector3.one * 0.12f, Vector3.zero, eyeWhiteMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Pupil Left", new Vector3(-0.16f, 1.22f, 0.485f), Vector3.one * 0.055f, Vector3.zero, noseMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Sphere, "Pupil Right", new Vector3(0.16f, 1.22f, 0.485f), Vector3.one * 0.055f, Vector3.zero, noseMaterial);

        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Front Leg Left", new Vector3(-0.2f, 0.25f, 0.22f), new Vector3(0.14f, 0.28f, 0.14f), Vector3.zero, bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Front Leg Right", new Vector3(0.2f, 0.25f, 0.22f), new Vector3(0.14f, 0.28f, 0.14f), Vector3.zero, bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Back Leg Left", new Vector3(-0.2f, 0.25f, -0.22f), new Vector3(0.14f, 0.28f, 0.14f), Vector3.zero, bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Back Leg Right", new Vector3(0.2f, 0.25f, -0.22f), new Vector3(0.14f, 0.28f, 0.14f), Vector3.zero, bodyMaterial);
        CreateDogPart(dog.transform, PrimitiveType.Capsule, "Tail", new Vector3(0f, 0.78f, -0.48f), new Vector3(0.14f, 0.36f, 0.14f), new Vector3(-42f, 0f, 0f), bodyMaterial);
    }

    private static void CreateDogPart(Transform parent, PrimitiveType primitiveType, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
    {
        CreateDogPart(parent, primitiveType, objectName, localPosition, localScale, Vector3.zero, material);
    }

    private static void CreateDogPart(Transform parent, PrimitiveType primitiveType, string objectName, Vector3 localPosition, Vector3 localScale, Vector3 localEulerAngles, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = Quaternion.Euler(localEulerAngles);

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            color = color
        };
        return material;
    }
}
