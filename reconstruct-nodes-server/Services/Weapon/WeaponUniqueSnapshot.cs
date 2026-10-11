namespace ReconstructNodesServer.Services
{
    internal sealed record WeaponUniqueSnapshot(string UniqueKey, IReadOnlyList<int> Values);
}
