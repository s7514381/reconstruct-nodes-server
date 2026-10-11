using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class PlayerCompleteModel : PlayerModel
    {
        public List<GameCollectionModel> GameCollectionList { get; set; } = new List<GameCollectionModel>();
        public List<MapModel> MapList { get; set; } = new List<MapModel>();
        public List<AchievementModel> AchievementList { get; set; } = new List<AchievementModel>();
        public List<EquipmentModel> EquipmentList { get; set; } = new List<EquipmentModel>();
        public List<AbilityModel> AbilityList { get; set; } = new List<AbilityModel>();
        public List<CharacterModel> CharacterList { get; set; } = new List<CharacterModel>();
        public List<AbilitySelectModel> AbilitySelectList { get; set; } = new List<AbilitySelectModel>();
        public List<PlayerParamsModel> PlayerParamsList { get; set; } = new List<PlayerParamsModel>();
    }
}
