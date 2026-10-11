using System.Data;
using System.Data.SqlClient;

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

        public IDbConnection GetOpenConnection()
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("未設定資料庫連線字串 (DefaultConnection)。");
            }

            IDbConnection conn = SQLType switch
            {
                SQLType.SQLSERVER => new SqlConnection(_connectionString),
                _ => throw new NotSupportedException($"不支援的 SQLType: {SQLType}")
            };

            conn.Open();
            return conn;
        }

        public IDbConnection ReConnection(IDbConnection conn)
        {
            conn?.Close();
            conn?.Dispose();
            return GetOpenConnection();
        }

    }

    public enum SQLType
    {
        SQLSERVER
    }
}
