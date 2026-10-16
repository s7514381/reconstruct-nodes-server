using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Services
{
    /// <summary>CharacterSkillTree 資料層；純子表，僅使用 _RowService 基底方法。</summary>
    public class CharacterSkillTreeService(BaseServiceArgument argument) : _RowService<CharacterSkillTree>(argument)
    {
    }
}
