using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Models;

public sealed class PlayerInitializationResponse
{
    [JsonPropertyName("characters")]
    public required List<CharacterModel> Characters { get; init; }
    [JsonPropertyName("gold")]
    public required long Gold { get; init; }
    [JsonPropertyName("catalog")]
    public required ShopCommand.ShopCatalog Catalog { get; init; }
    // null represents a missing save, not a failed read.
    [JsonPropertyName("progress")]
    public required GameProgressModel? Progress { get; init; }
    [JsonPropertyName("talent")]
    public required TalentModel? Talent { get; init; }
    [JsonPropertyName("achievement")]
    public required AchievementProgressModel? Achievement { get; init; }
    [JsonPropertyName("items")]
    public required List<Item> Items { get; init; }
    [JsonPropertyName("weapons")]
    public required List<WeaponInstanceModel> Weapons { get; init; }
    [JsonPropertyName("revisions")]
    public required RevisionVector Revisions { get; init; }
}
