using UnityEngine;

/// <summary>
/// The single SDK-free bridge from Q2/Q3 space providers into the current dog and ball logic.
/// </summary>
[DisallowMultipleComponent]
public sealed class PetSpaceContractBinder : MonoBehaviour
{
    [SerializeField] private MonoBehaviour providerBehaviour;
    [SerializeField] private ShepherdCompanionAI companion;
    [SerializeField] private FetchBallInteraction ball;

    private IPetSpaceContractProvider provider;
    private int lastAppliedRevision = -1;
    private bool spawnApplied;

    public void Configure(
        MonoBehaviour providerReference,
        ShepherdCompanionAI companionReference,
        FetchBallInteraction ballReference)
    {
        Unsubscribe();
        providerBehaviour = providerReference;
        companion = companionReference;
        ball = ballReference;
        Subscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        provider = providerBehaviour as IPetSpaceContractProvider;
        if (provider == null)
            return;

        provider.SnapshotChanged -= Apply;
        provider.SnapshotChanged += Apply;
        Apply(provider.Current);
    }

    private void Unsubscribe()
    {
        if (provider != null)
            provider.SnapshotChanged -= Apply;
        provider = null;
    }

    private void Apply(PetSpaceContractSnapshot snapshot)
    {
        if (snapshot.Revision == lastAppliedRevision)
            return;
        lastAppliedRevision = snapshot.Revision;

        if (snapshot.State != PetSpaceContractState.Valid)
            return;

        if (companion == null)
            companion = FindFirstObjectByType<ShepherdCompanionAI>();
        if (ball == null)
            ball = FindFirstObjectByType<FetchBallInteraction>();

        companion?.ApplyPetSpaceContract(snapshot, !spawnApplied);
        ball?.ApplyPetSpaceContract(snapshot);
        spawnApplied = true;
    }
}
