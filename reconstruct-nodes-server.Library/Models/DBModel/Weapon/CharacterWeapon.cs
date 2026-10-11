using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    /// <summary>
    /// 玩家各角色的裝備武器，只存 EntryKey 參照 Weapon 列；未裝備時 EntryKey 為 null
    /// </summary>
    [Table(nameof(CharacterWeapon))]
    public class CharacterWeapon : ModelBase
    {
        public int CharacterKind { get => Get<int>(); set => Set(value); }

        public string EntryKey { get => Get<string>(); set => Set(value); }
    }
}
