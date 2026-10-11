using System;
using System.Data;

namespace ReconstructNodesServer.Repositories
{
    /// <summary>
    /// 與資料庫管理物件連動的包裝後 Transaction
    /// </summary>
    public class ManagedTransaction : IDisposable
    {
        private readonly IDbTransaction _transaction;
        private readonly IDapperRepository _repository;

        public ManagedTransaction(IDbTransaction transaction, IDapperRepository repository)
        {
            _transaction = transaction;
            _repository = repository;
        }

        /// <summary>
        /// 釋放 Transaction 並清空 Repository 的 Transaction 欄位
        /// </summary>
        public void Dispose()
        {
            _transaction.Dispose();
            _repository.Transaction = null;
        }

        public void Commit()
        {
            _transaction.Commit();
        }

        public void Rollback()
        {
            _transaction.Rollback();
        }
    }
}
