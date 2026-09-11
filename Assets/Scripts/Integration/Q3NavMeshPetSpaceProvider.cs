using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Publishes Q3 room positions and complete paths from MRUK SceneNavigation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SceneNavigation))]
public sealed class Q3NavMeshPetSpaceProvider : MonoBehaviour, IPetSpaceContractProvider
{
    [SerializeField] private int agentTypeId;
    [SerializeField, Min(0.01f)] private float sampleSearchRadius = 0.5f;
    [SerializeField, Min(3)] private int maxCandidateAttempts = 32;
    [SerializeField, Min(0f)] private float minimumDestinationDistance = 1f;

    private readonly List<Vector3> candidates = new List<Vector3>();
    private SceneNavigation sceneNavigation;
    private PetSpaceContractSnapshot current;
    private bool navMeshReady;

    public PetSpaceContractSnapshot Current => current;
    public event Action<PetSpaceContractSnapshot> SnapshotChanged;

    private void Awake()
    {
        sceneNavigation = GetComponent<SceneNavigation>();
        PublishPending("Waiting for MRUK SceneNavigation NavMesh.");
    }

    private void OnEnable()
    {
        if (sceneNavigation == null)
            sceneNavigation = GetComponent<SceneNavigation>();
        sceneNavigation.OnNavMeshInitialized.AddListener(HandleNavMeshInitialized);
    }

    private void OnDisable()
    {
        if (sceneNavigation != null)
            sceneNavigation.OnNavMeshInitialized.RemoveListener(HandleNavMeshInitialized);
    }

    public void RequestNewContract()
    {
        if (!navMeshReady)
        {
            PublishPending("MRUK NavMesh is not ready.");
            return;
        }

        Evaluate();
    }

    private void HandleNavMeshInitialized()
    {
        navMeshReady = true;
        Evaluate();
    }

    private void Evaluate()
    {
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null)
        {
            PublishPending("No current MRUK room data.");
            return;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.indices == null || triangulation.indices.Length < 3)
        {
            PublishInvalid(PetSpaceInvalidReasonCode.NoNavMesh, "MRUK NavMesh has no triangles.");
            return;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = NavMesh.AllAreas
        };
        List<float> triangleAreas = new List<float>();
        float totalArea = 0f;
        for (int i = 0; i < triangulation.indices.Length; i += 3)
        {
            Vector3 a = triangulation.vertices[triangulation.indices[i]];
            Vector3 b = triangulation.vertices[triangulation.indices[i + 1]];
            Vector3 c = triangulation.vertices[triangulation.indices[i + 2]];
            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            triangleAreas.Add(area);
            totalArea += area;
        }

        if (totalArea <= 0f)
        {
            PublishInvalid(PetSpaceInvalidReasonCode.NoNavMesh, "MRUK NavMesh has no usable surface area.");
            return;
        }

        candidates.Clear();
        for (int attempt = 0; attempt < maxCandidateAttempts && candidates.Count < 8; attempt++)
        {
            int triangle = PickAreaWeightedTriangle(triangleAreas, totalArea);
            int i0 = triangulation.indices[triangle * 3];
            int i1 = triangulation.indices[triangle * 3 + 1];
            int i2 = triangulation.indices[triangle * 3 + 2];
            float u = UnityEngine.Random.value;
            float v = UnityEngine.Random.value;
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }

            Vector3 raw = triangulation.vertices[i0] +
                          u * (triangulation.vertices[i1] - triangulation.vertices[i0]) +
                          v * (triangulation.vertices[i2] - triangulation.vertices[i0]);
            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, sampleSearchRadius, filter))
                continue;
            if (!room.IsPositionInRoom(hit.position, false) || !IsDistinct(hit.position))
                continue;
            candidates.Add(hit.position);
        }

        if (candidates.Count < 3)
        {
            PublishInvalid(PetSpaceInvalidReasonCode.CandidateNotUsable, "Fewer than three usable room candidates.");
            return;
        }

        Vector3 spawn = candidates[0];
        Vector3 destination = FarthestFrom(spawn);
        Vector3 recovery = NearestDifferent(spawn);
        if ((destination - spawn).sqrMagnitude < minimumDestinationDistance * minimumDestinationDistance)
        {
            PublishInvalid(PetSpaceInvalidReasonCode.CandidateNotUsable, "Destination is too close to spawn.");
            return;
        }

        if (!TryCompletePath(spawn, destination, filter, out NavMeshPath destinationPath) ||
            !TryCompletePath(spawn, recovery, filter, out NavMeshPath recoveryPath))
        {
            PublishInvalid(PetSpaceInvalidReasonCode.NoCompletePath, "A complete destination or recovery path was not found.");
            return;
        }

        Publish(new PetSpaceContractSnapshot
        {
            State = PetSpaceContractState.Valid,
            InvalidReasonCode = PetSpaceInvalidReasonCode.None,
            InvalidReason = string.Empty,
            HasSpawnPosition = true,
            SpawnPosition = new Pose(spawn, Quaternion.identity),
            HasDestinationPosition = true,
            DestinationPosition = new Pose(destination, Quaternion.identity),
            HasDestinationPath = true,
            DestinationPathCorners = destinationPath.corners,
            HasRecoveryPosition = true,
            RecoveryPosition = new Pose(recovery, Quaternion.identity),
            HasRecoveryPath = true,
            RecoveryPathCorners = recoveryPath.corners,
            CandidateCount = candidates.Count,
            Revision = current.Revision + 1
        });
    }

    private void PublishPending(string reason)
    {
        Publish(new PetSpaceContractSnapshot
        {
            State = PetSpaceContractState.Pending,
            InvalidReasonCode = PetSpaceInvalidReasonCode.ProviderUnavailable,
            InvalidReason = reason,
            Revision = current.Revision + 1
        });
    }

    private void PublishInvalid(PetSpaceInvalidReasonCode code, string reason)
    {
        Publish(new PetSpaceContractSnapshot
        {
            State = PetSpaceContractState.Invalid,
            InvalidReasonCode = code,
            InvalidReason = reason,
            Revision = current.Revision + 1
        });
    }

    private void Publish(PetSpaceContractSnapshot snapshot)
    {
        current = snapshot;
        SnapshotChanged?.Invoke(current);
    }

    private static int PickAreaWeightedTriangle(List<float> areas, float totalArea)
    {
        float value = UnityEngine.Random.Range(0f, Mathf.Max(float.Epsilon, totalArea));
        for (int i = 0; i < areas.Count - 1; i++)
        {
            value -= areas[i];
            if (value <= 0f)
                return i;
        }
        return areas.Count - 1;
    }

    private bool IsDistinct(Vector3 position)
    {
        float minimum = Mathf.Max(0.05f, minimumDestinationDistance * 0.25f);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (Vector2.Distance(
                    new Vector2(position.x, position.z),
                    new Vector2(candidates[i].x, candidates[i].z)) < minimum)
                return false;
        }
        return true;
    }

    private Vector3 FarthestFrom(Vector3 origin)
    {
        Vector3 result = origin;
        float best = -1f;
        for (int i = 0; i < candidates.Count; i++)
        {
            float distance = (candidates[i] - origin).sqrMagnitude;
            if (distance <= best)
                continue;
            best = distance;
            result = candidates[i];
        }
        return result;
    }

    private Vector3 NearestDifferent(Vector3 origin)
    {
        Vector3 result = origin;
        float best = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            float distance = (candidates[i] - origin).sqrMagnitude;
            if (distance < 0.0001f || distance >= best)
                continue;
            best = distance;
            result = candidates[i];
        }
        return result;
    }

    private static bool TryCompletePath(
        Vector3 from,
        Vector3 to,
        NavMeshQueryFilter filter,
        out NavMeshPath path)
    {
        path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, filter, path) &&
               path.status == NavMeshPathStatus.PathComplete &&
               path.corners != null && path.corners.Length > 1;
    }
}
