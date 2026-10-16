using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Services
{
    /// <summary>EconomyRequest 資料層；純子表，僅使用 _RowService 基底方法。</summary>
    public class EconomyRequestService(BaseServiceArgument argument) : _RowService<EconomyRequest>(argument)
    {
    }
}
