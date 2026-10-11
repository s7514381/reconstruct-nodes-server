namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 詞綴的內部表示；Rolled 為 false 時代表前端的純字串格式（tier 0），Values 為擲骰數值。
    /// 必須為 internal：Program.cs 會把 Services 命名空間下的 public 類別全部註冊為 DI 服務，record 會因建構參數無法解析而失敗。
    /// </summary>
    internal sealed record WeaponAffixSnapshot(string AffixKey, bool Rolled, IReadOnlyList<int> Values);
}
