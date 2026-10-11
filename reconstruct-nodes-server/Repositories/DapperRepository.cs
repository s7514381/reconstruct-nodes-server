using Dapper;
using Dapper.Contrib.Extensions;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Repositories
{
    public class DapperRepository : IDapperRepository
    {
        public IDbConnection Connection { get; protected set; }

        /// <summary>
        /// 當前正在使用的 Transaction
        /// </summary>
        public IDbTransaction? Transaction { get; set; }
        public DapperDBContext _dbContext { get; protected set; }

        public DapperRepository(DapperDBContext dbContext)
        {
            _dbContext = dbContext;
            Connection = dbContext.GetOpenConnection();
        }

        public void Dispose()
        {
            Transaction?.Dispose();
            Connection?.Close();
            Connection?.Dispose();
        }

        public ManagedTransaction OpenTransaction()
        {
            BaseFunction.SpeedDebug(() => {
                Connection = _dbContext.ReConnection(Connection);
                Transaction = Connection.BeginTransaction();
            });
            return new ManagedTransaction(Transaction ?? throw new InvalidOperationException("無法開啟 Transaction。"), this);
        }

        public void ClearTransaction()
        {
            if (Transaction != null)
            {
                Transaction = null;
            }
        }

        public IEnumerable<T> Query<T>(string sql, object? param = null)
        {
            return Connection.Query<T>(sql, param, Transaction);
        }

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
        {
            return Connection.QueryAsync<T>(sql, param, Transaction);
        }

        public T QueryFirst<T>(string sql, object? param = null)
        {
            return Connection.QueryFirst<T>(sql, param, Transaction);
        }

        public Task<T> QueryFirstAsync<T>(string sql, object? param = null)
        {
            return Connection.QueryFirstAsync<T>(sql, param, Transaction);
        }

        public T QueryFirstOrDefault<T>(string sql, object? param = null)
        {
            return Connection.QueryFirstOrDefault<T>(sql, param, Transaction);
        }

        public Task<T> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
        {
            return Connection.QueryFirstOrDefaultAsync<T>(sql, param, Transaction);
        }

        public int Execute(string sql, object? param = null)
        {
            return Connection.Execute(sql, param, Transaction);
        }

        public Task<int> ExecuteAsync(string sql, object? param = null)
        {
            return Connection.ExecuteAsync(sql, param, Transaction);
        }

        public long Insert<T>(T entityToInsert) where T : class
        {
            return Connection.Insert(entityToInsert, Transaction);
        }

        public Task<int> InsertAsync<T>(T entityToInsert) where T : class
        {
            return Connection.InsertAsync(entityToInsert, Transaction);
        }

        public Task<bool> UpdateAsync<T>(T entity) where T : class
        {
            return Connection.UpdateAsync(entity, Transaction);
        }

        public SqlMapper.GridReader QueryMultiple(string sql, object? param = null)
        {
            return Connection.QueryMultiple(sql, param, Transaction);
        }

        public Task<SqlMapper.GridReader> QueryMultipleAsync(string sql, object? param = null)
        {
            return Connection.QueryMultipleAsync(sql, param, Transaction);
        }
    }
}
