#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models;

/// <summary>Persistence revision, independent of game schema/content versions.</summary>
public interface IRevisionedSnapshot
{
    long Revision { get; set; }
    long? ExpectedRevision { get; set; }
}

public abstract class RevisionedSnapshot : IRevisionedSnapshot
{
    [JsonPropertyName("revision")]
    public long Revision { get; set; }
    [JsonPropertyName("expected_revision")]
    public long? ExpectedRevision { get; set; }
}

public class CommandRequest
{
    [JsonPropertyName("request_id")]
    [JsonRequired]
    public Guid RequestId { get; set; }
}

public class ExpectedRevisionRequest : CommandRequest
{
    [JsonPropertyName("expected_revision")]
    [JsonRequired]
    public long ExpectedRevision { get; set; }
}

public sealed class WeaponAcquireRequest : CommandRequest
{
    [JsonPropertyName("weapon")]
    [JsonRequired]
    public WeaponInstanceModel Weapon { get; set; } = new();
}

public sealed class WeaponEquipRequest : CommandRequest
{
    [JsonPropertyName("character_id")]
    public Guid CharacterId { get; set; }
    // null means unequip.
    [JsonPropertyName("weapon_entry_id")]
    public string? WeaponEntryId { get; set; }
}

public sealed class SkillLoadoutRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("character_id")]
    public Guid CharacterId { get; set; }
    [JsonPropertyName("skill_trees")]
    [JsonRequired]
    public List<GameProgressSkillLoadoutModel> SkillTrees { get; set; } = [];
}

public sealed class AchievementClaimRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("achievement_item_id")]
    public string AchievementItemId { get; set; } = "";
}

/// <summary>領取成就的單一回執：成就推導快照與領取後的在庫物品，客戶端不必再讀 Item 清單。</summary>
public sealed class AchievementClaimSnapshot
{
    [JsonPropertyName("progress")]
    public AchievementProgressModel Progress { get; set; } = new();
    [JsonPropertyName("items")]
    public List<ReconstructNodesServer.Library.DBModel.Item> Items { get; set; } = [];
}

public sealed class TalentNodeCommandRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("tree_id")]
    public string TreeId { get; set; } = "";
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = "";
}

public sealed class TalentResetRequest : ExpectedRevisionRequest
{
    // null resets all trees.
    [JsonPropertyName("tree_id")]
    public string? TreeId { get; set; }
}

public sealed class DeploymentCommandRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("deployment_log")]
    [JsonRequired]
    public List<GameProgressDeploymentModel> DeploymentLog { get; set; } = [];
}

public sealed class BattlefieldCommandRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("battlefield")]
    [JsonRequired]
    public GameProgressBattlefieldModel Battlefield { get; set; } = new();
    // Editor can atomically commit roads, withdrawals and placements together.
    [JsonPropertyName("deployment_log")]
    public List<GameProgressDeploymentModel>? DeploymentLog { get; set; }
}

public sealed class WaveTransitionRequest : ExpectedRevisionRequest
{
    [JsonPropertyName("wave")]
    public int Wave { get; set; }
    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }
    [JsonPropertyName("challenge")]
    public bool Challenge { get; set; }
}

public sealed class WaveTransitionSnapshot
{
    [JsonPropertyName("progress")]
    public GameProgressModel Progress { get; set; } = new();
    [JsonPropertyName("session")]
    public WaveStartResult Session { get; set; } = new();
}

public sealed class WeaponRefineRequest : CommandRequest
{
    [JsonPropertyName("weapon_entry_id")]
    public string WeaponEntryId { get; set; } = "";
    [JsonPropertyName("crystal_id")]
    public string CrystalId { get; set; } = "";
}

public sealed class WeaponRefineSnapshot
{
    [JsonPropertyName("weapon")]
    public WeaponInstanceModel Weapon { get; set; } = new();
    [JsonPropertyName("crystal_id")]
    public string CrystalId { get; set; } = "";
    [JsonPropertyName("material_count")]
    public int MaterialCount { get; set; }
}

public sealed class WeaponDisassembleRequest : CommandRequest
{
    [JsonPropertyName("weapon_entry_ids")]
    public List<string> WeaponEntryIds { get; set; } = [];
}

public sealed class WeaponDisassembleSnapshot
{
    [JsonPropertyName("removed_ids")]
    public List<string> RemovedIds { get; set; } = [];
    [JsonPropertyName("skipped_ids")]
    public List<string> SkippedIds { get; set; } = [];
    [JsonPropertyName("weapons")]
    public List<WeaponInstanceModel> Weapons { get; set; } = [];
}

public sealed class RevisionVector
{
    [JsonPropertyName("talent")]
    public long Talent { get; set; }
    [JsonPropertyName("character")]
    public long Character { get; set; }
    [JsonPropertyName("game_progress")]
    public long GameProgress { get; set; }
    [JsonPropertyName("achievement")]
    public long Achievement { get; set; }
    [JsonPropertyName("weapon")]
    public long Weapon { get; set; }
}

public sealed class CommandReceipt<T>
{
    [JsonPropertyName("request_id")]
    public Guid RequestId { get; set; }
    [JsonPropertyName("revision")]
    public long Revision { get; set; }
    [JsonPropertyName("snapshot")]
    public T Snapshot { get; set; } = default!;
}
