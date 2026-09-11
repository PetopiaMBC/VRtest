using System;
using UnityEngine;

public enum PetSpaceContractState
{
    Pending = 0,
    Invalid = 1,
    Valid = 2
}

public enum PetSpaceInvalidReasonCode
{
    None,
    Unknown,
    ProviderUnavailable,
    NoSpaceData,
    MissingReference,
    InvalidCoordinate,
    NoRecoveryPosition,
    NoNavMesh,
    NoCompletePath,
    CandidateNotUsable
}

[Serializable]
public struct PetSpaceContractSnapshot
{
    public PetSpaceContractState State;
    public PetSpaceInvalidReasonCode InvalidReasonCode;
    public string InvalidReason;
    public bool HasSpawnPosition;
    public Pose SpawnPosition;
    public bool HasWalkArea;
    public Bounds WalkArea;
    public bool HasRestPosition;
    public Pose RestPosition;
    public bool HasDestinationPosition;
    public Pose DestinationPosition;
    public bool HasDestinationPath;
    public Vector3[] DestinationPathCorners;
    public bool HasRecoveryPath;
    public Vector3[] RecoveryPathCorners;
    public bool HasRecoveryPosition;
    public Pose RecoveryPosition;
    public int CandidateCount;
    public int Revision;
}

public interface IPetSpaceContractProvider
{
    PetSpaceContractSnapshot Current { get; }
    event Action<PetSpaceContractSnapshot> SnapshotChanged;
}
