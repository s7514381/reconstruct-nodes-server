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
        /// 分類；僅在列建立時或分類仍為空白時寫入
        /// </summary>
        public string? Category { get; set; }
    }
}
