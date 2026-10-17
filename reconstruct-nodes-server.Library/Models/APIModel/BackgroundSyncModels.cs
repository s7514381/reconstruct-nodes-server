#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models;

/// <summary>
/// A frozen batch of background work, not a replacement for foreground commands.
/// Absent work is an empty list, or null for the single wave start.
/// </summary>
public sealed class BackgroundSyncRequest : CommandRequest
{
    /// <summary>The wave start doubles as the session ID of the wave it opens.</summary>
    [JsonPropertyName("wave_start")]
    public WaveStartRequest? WaveStart { get; set; }
    [JsonPropertyName("kills")]
    public List<SessionKillsRequest> Kills { get; set; } = [];
    [JsonPropertyName("items")]
    public List<ItemDeltaRequest> Items { get; set; } = [];
    [JsonPropertyName("weapon_acquisitions")]
    public List<WeaponAcquireRequest> WeaponAcquisitions { get; set; } = [];
}

/// <summary>Confirms the entire request ID; no confirmation is issued before execution succeeds.</summary>
public sealed class BackgroundSyncResponse : CommandRequest
{
    [JsonPropertyName("gold")]
    public long? Gold { get; set; }
    /// <summary>The session opened by the request's wave start; null when the batch had none.</summary>
    [JsonPropertyName("session")]
    public WaveStartResult? Session { get; set; }
    [JsonPropertyName("progress")]
    public GameProgressModel? Progress { get; set; }
    /// <summary>Only present when kills in this batch earned talent points.</summary>
    [JsonPropertyName("talent")]
    public TalentModel? Talent { get; set; }
    [JsonPropertyName("acquisitions")]
    public List<CommandReceipt<WeaponInstanceModel>> Acquisitions { get; set; } = [];
}
