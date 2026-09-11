using System;
using UnityEngine;

/// <summary>
/// Publishes authored Q2 virtual-room points without referencing Meta or MRUK APIs.
/// </summary>
[DisallowMultipleComponent]
public sealed class Q2VirtualRoomPetSpaceProvider : MonoBehaviour, IPetSpaceContractProvider
{
    [SerializeField] private Transform dogSpawnPosition;
    [SerializeField] private Transform dogRestPosition;
    [SerializeField] private Transform dogRecoveryPosition;
    [SerializeField] private BoxCollider dogWalkArea;
    [SerializeField, Min(0f)] private float boundaryInset = 0.3f;

    private PetSpaceContractSnapshot current;

    public PetSpaceContractSnapshot Current => current;
    public event Action<PetSpaceContractSnapshot> SnapshotChanged;

    private void OnEnable()
    {
        Refresh();
    }

    public void Configure(
        Transform spawn,
        Transform rest,
        Transform recovery,
        BoxCollider walkArea)
    {
        dogSpawnPosition = spawn;
        dogRestPosition = rest;
        dogRecoveryPosition = recovery;
        dogWalkArea = walkArea;
        Refresh();
    }

    public void Refresh()
    {
        int revision = current.Revision + 1;
        if (dogSpawnPosition == null || dogRestPosition == null ||
            dogRecoveryPosition == null || dogWalkArea == null)
        {
            Publish(new PetSpaceContractSnapshot
            {
                State = PetSpaceContractState.Invalid,
                InvalidReasonCode = PetSpaceInvalidReasonCode.MissingReference,
                InvalidReason = "Q2 Spawn/Rest/Recovery/WalkArea reference is missing.",
                Revision = revision
            });
            return;
        }

        Bounds bounds = dogWalkArea.bounds;
        Vector3 size = bounds.size;
        size.x = Mathf.Max(0.1f, size.x - boundaryInset * 2f);
        size.z = Mathf.Max(0.1f, size.z - boundaryInset * 2f);
        bounds.size = size;

        Publish(new PetSpaceContractSnapshot
        {
            State = PetSpaceContractState.Valid,
            InvalidReasonCode = PetSpaceInvalidReasonCode.None,
            InvalidReason = string.Empty,
            HasSpawnPosition = true,
            SpawnPosition = new Pose(dogSpawnPosition.position, dogSpawnPosition.rotation),
            HasWalkArea = true,
            WalkArea = bounds,
            HasRestPosition = true,
            RestPosition = new Pose(dogRestPosition.position, dogRestPosition.rotation),
            HasRecoveryPosition = true,
            RecoveryPosition = new Pose(dogRecoveryPosition.position, dogRecoveryPosition.rotation),
            Revision = revision
        });
    }

    private void Publish(PetSpaceContractSnapshot snapshot)
    {
        current = snapshot;
        SnapshotChanged?.Invoke(current);
    }
}
