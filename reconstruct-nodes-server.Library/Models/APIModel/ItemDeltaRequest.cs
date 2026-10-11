namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 物品增量請求：Delta 為正數代表取得，負數代表消耗
    /// </summary>
    public class ItemDeltaRequest
    {
        public string ItemKey { get; set; } = string.Empty;

        public int Delta { get; set; }

        /// <summary>
        /// 顯示名稱與分類；僅在列建立時或名稱仍為空白時寫入，既有名稱不被覆寫
        /// </summary>
        public string? Name { get; set; }

        public string? Category { get; set; }
    }
}
