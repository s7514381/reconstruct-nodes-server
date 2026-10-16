using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Utility;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 「屬於玩家的資料列」查詢的唯一 SQL 來源。表名（取自 DBModel 型別，與 [Table(nameof(T))] 一致）、
    /// 擁有者過濾 (@CreateUserId)、軟刪除過濾 (Status != -1) 都只在這裡定義，
    /// 各 Command 只提供額外條件；之後要改底層（加索引提示、改過濾規則）只需改這個檔案。
    /// 參數一律走 Dapper 參數；condition / columns / orderBy 只能是固定 SQL 片段，不可拼入使用者輸入。
    /// </summary>
    public static class OwnedSql
    {
        private const string OwnerFilter = "CreateUserId = @CreateUserId";
        private const string ActiveFilter = "Status != -1";

        /// <summary>表名一律取自 DBModel 的 [Table] 屬性。</summary>
        public static string Table<T>()
        {
            string table = CommonUtility.GetTableAttribute<T>();
            return !string.IsNullOrEmpty(table) ? table
                : throw new InvalidOperationException($"{typeof(T).Name} 未設定資料表");
        }

        /// <summary>
        /// <c>SELECT [TOP n] {columns} FROM {T} WHERE 擁有者 [AND 未刪除] [AND 條件] [ORDER BY ...];</c>
        /// </summary>
        /// <param name="activeOnly">是否加上 Status != -1（預設加；無軟刪除語意的資料如 EconomyRequest 傳 false）。</param>
        public static string Select<T>(string? condition = null, string columns = "*", string? orderBy = null,
            int? top = null, bool activeOnly = true, string? tableHint = null) =>
            $"SELECT {(top is { } n ? $"TOP {n} " : "")}{columns} FROM {Table<T>()}{(tableHint == null ? "" : $" WITH ({tableHint})")} WHERE {Where(condition, activeOnly)}" +
            $"{(string.IsNullOrWhiteSpace(orderBy) ? "" : $" ORDER BY {orderBy}")};";

        /// <summary>WHERE 之後的內容，供 EXISTS / JOIN / DELETE 等自訂形狀沿用同一組過濾。</summary>
        public static string Where(string? condition = null, bool activeOnly = true) =>
            Join(OwnerFilter, activeOnly ? ActiveFilter : null, Group(condition));

        /// <summary>批次讀取：整份結果集解析為 List&lt;T&gt; 後交給 assign。</summary>
        public static SnapshotRead<List<T>> Rows<T>(string? condition, Action<List<T>> assign,
            string? orderBy = null, bool activeOnly = true) =>
            new(Select<T>(condition, orderBy: orderBy, activeOnly: activeOnly), async reader =>
            {
                var rows = (await reader.ReadAsync<T>()).ToList();
                assign(rows);
                return rows;
            });

        /// <summary>
        /// 子列查詢：<c>SELECT c.* FROM TChild c JOIN TParent p</c>，父子雙方都限制為本人。
        /// 外鍵欄位取自 TChild 上的 [Relation]；condition 以 c. / p. 別名撰寫。
        /// </summary>
        public static string Children<TChild, TParent>(bool activeChildren = false, bool activeParents = true,
            string? condition = null, string? orderBy = "c.Sequence", string columns = "c.*") =>
            $"SELECT {columns} FROM {Table<TChild>()} c INNER JOIN {Table<TParent>()} p ON p.Id = c.{ForeignKey<TChild, TParent>()} " +
            $"WHERE {Join("p.CreateUserId = @CreateUserId", "c.CreateUserId = @CreateUserId",
                activeParents ? "p.Status != -1" : null, activeChildren ? "c.Status != -1" : null, Group(condition))}" +
            $"{OrderBy(orderBy)};";

        /// <summary>孫列查詢（TChild → TMiddle → TRoot），三層都限制為本人；各層狀態過濾由呼叫端指定。</summary>
        public static string Descendants<TChild, TMiddle, TRoot>(bool activeChildren = false, bool activeMiddle = false,
            bool activeRoot = true, string? condition = null, string orderBy = "c.Sequence") =>
            $"SELECT c.* FROM {Table<TChild>()} c INNER JOIN {Table<TMiddle>()} m ON m.Id = c.{ForeignKey<TChild, TMiddle>()} " +
            $"INNER JOIN {Table<TRoot>()} r ON r.Id = m.{ForeignKey<TMiddle, TRoot>()} " +
            $"WHERE {Join("r.CreateUserId = @CreateUserId", "m.CreateUserId = @CreateUserId", "c.CreateUserId = @CreateUserId",
                activeRoot ? "r.Status != -1" : null, activeMiddle ? "m.Status != -1" : null,
                activeChildren ? "c.Status != -1" : null, Group(condition))}{OrderBy(orderBy)};";

        public static SnapshotRead<List<TChild>> ChildrenRows<TChild, TParent>(Action<List<TChild>> assign,
            bool activeChildren = false, bool activeParents = true, string? condition = null) =>
            new(Children<TChild, TParent>(activeChildren, activeParents, condition), async reader =>
            {
                var rows = (await reader.ReadAsync<TChild>()).ToList();
                assign(rows);
                return rows;
            });

        /// <summary>刪除本人資料（不分狀態）；condition 為額外條件。</summary>
        public static string Delete<T>(string? condition = null) =>
            $"DELETE FROM {Table<T>()} WHERE {Where(condition, activeOnly: false)};";

        /// <summary>
        /// 批次開頭的擁有者檢查：玩家不存在就中止。what 只放固定識別字（如 "Wave"），用於錯誤訊息。
        /// </summary>
        public static string RequireOwner(string what) => $"""
            DECLARE @Owner uniqueidentifier;
            SELECT @Owner = Id FROM {Table<Player>()} WHERE Id = @CreateUserId;
            IF @Owner IS NULL THROW 51002, '{what} owner is unavailable', 1;
            """;

        /// <summary>RequireOwner 的批次讀取版（不產生結果集）。</summary>
        public static SnapshotRead<object?> OwnerRead(string what) =>
            new(RequireOwner(what), _ => Task.FromResult<object?>(null));

        public static string ForeignKey<TChild, TParent>()
        {
            string field = CommonUtility.GetForeignKeyField<TChild, TParent>();
            return !string.IsNullOrEmpty(field) ? field
                : throw new InvalidOperationException($"{typeof(TChild).Name} 未設定 {typeof(TParent).Name} 關聯");
        }

        // 額外條件一律加括號，避免其中的 OR 吃掉擁有者過濾。
        private static string? Group(string? condition) =>
            string.IsNullOrWhiteSpace(condition) ? null : $"({condition})";
        private static string Join(params string?[] parts) => string.Join(" AND ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
        private static string OrderBy(string? orderBy) => string.IsNullOrWhiteSpace(orderBy) ? "" : $" ORDER BY {orderBy}";
    }
}
