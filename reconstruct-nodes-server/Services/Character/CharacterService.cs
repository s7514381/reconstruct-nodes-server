
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using System.Diagnostics;
using System.Linq;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class CharacterService : _ModelService<Character, CharacterModel>
    {
        public CharacterService(BaseServiceArgument argument) : base(argument) { }
    }
}
