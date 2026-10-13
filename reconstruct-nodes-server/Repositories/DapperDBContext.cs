using System.Data;
using System.Data.SqlClient;
using System.Data.Common;

namespace ReconstructNodesServer.Repositories
{
    public class DapperDBContext
    {
        private readonly string? _connectionString;

        public SQLType SQLType = SQLType.SQLSERVER;

        public DapperDBContext(string? connectionString)
        {
            _connectionString = connectionString;
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
