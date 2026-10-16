#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models;

/// <summary>A frozen batch of background work, not a replacement for foreground commands.</summary>
public sealed class BackgroundSyncRequest : CommandRequest
{
    [JsonPropertyName("kills")]
    public List<EnemyKillRequest> Kills { get; set; } = [];
    [JsonPropertyName("wave_starts")]
    public List<WaveStartRequest> WaveStarts { get; set; } = [];
    [JsonPropertyName("progress")]
    public GameProgressModel? Progress { get; set; }
    [JsonPropertyName("talent")]
    public TalentModel? Talent { get; set; }
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
    [JsonPropertyName("sessions")]
    public List<WaveStartResult> Sessions { get; set; } = [];
    [JsonPropertyName("progress")]
    public GameProgressModel? Progress { get; set; }
    [JsonPropertyName("talent")]
    public TalentModel? Talent { get; set; }
    [JsonPropertyName("items")]
    public List<ReconstructNodesServer.Library.DBModel.Item>? Items { get; set; }
    [JsonPropertyName("weapons")]
    public List<WeaponInstanceModel>? Weapons { get; set; }
    [JsonPropertyName("acquisitions")]
    public List<CommandReceipt<WeaponInstanceModel>> Acquisitions { get; set; } = [];
}
