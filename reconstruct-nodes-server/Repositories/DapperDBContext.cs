using System.Data;
using System.Data.SqlClient;
using System.Data.Common;

namespace ReconstructNodesServer.Repositories
{
    public class DapperDBContext
    {
        // Azure SQL 建立實體連線（TLS + 登入 + 閘道轉址）約 0.4 秒以上；保留常駐連線，
        // 並行請求與閒置後的第一個請求才不必各自付出這個成本。
        private const int WarmPoolSize = 5;

        private readonly string? _connectionString;

        public SQLType SQLType = SQLType.SQLSERVER;

        public DapperDBContext(string? connectionString)
        {
            _connectionString = connectionString;
            if (!string.IsNullOrEmpty(connectionString))
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                if (builder.MinPoolSize == 0) { builder.MinPoolSize = WarmPoolSize; }
                _connectionString = builder.ConnectionString;
            }
        }

        /// <summary>啟動時開啟一次連線，讓連線池在背景補滿常駐連線。</summary>
        public void WarmPool()
        {
            using DbConnection connection = CreateConnection();
            connection.Open();
        }

        public virtual DbConnection CreateConnection()
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("未設定資料庫連線字串 (DefaultConnection)。");
            }

            return SQLType switch
            {
                SQLType.SQLSERVER => new SqlConnection(_connectionString),
                _ => throw new NotSupportedException($"不支援的 SQLType: {SQLType}")
            };

        }

    }

    public enum SQLType
    {
        SQLSERVER
    }
}
