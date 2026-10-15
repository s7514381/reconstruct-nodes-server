
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public class PlayerParamsService : _ModelService<PlayerParams, PlayerParamsModel>
    {
        public PlayerParamsService(BaseServiceArgument argument) : base(argument) 
        {
        }

        public override async Task Save(IEnumerable<PlayerParamsModel> list)
        {
            List<PlayerParamsModel> rows = list.ToList();
            await RejectGoldMutation(rows);
            await base.Save(rows);
        }

        public override async Task SaveData(IEnumerable<PlayerParams> list)
        {
            List<PlayerParams> rows = list.ToList();
            await RejectGoldMutation(rows);
            await base.SaveData(rows);
        }

        public override async Task DeleteModel(IEnumerable<PlayerParamsModel> list)
        {
            List<PlayerParamsModel> rows = list.ToList();
            await RejectGoldMutation(rows);
            await base.DeleteModel(rows);
        }

        private async Task RejectGoldMutation(IEnumerable<PlayerParams> rows)
        {
            if (rows.Any(row => (row.Group == "game_progress" && row.Name == "gold") ||
                row.Group is "economy" or RevisionService.RevisionGroup or "game_progress.unlocked_bullet_kinds" or "game_progress.unlocked_auxiliary_kinds"))
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyGoldManaged);
            }
            Guid[] ids = rows.Select(row => row.Id).ToArray();
            if (ids.Length == 0) { return; }
            var protectedIds = await QueryAsync<Guid>(
                @"SELECT Id FROM PlayerParams WHERE CreateUserId = @CreateUserId AND Id IN @Ids
                  AND (([Group] = 'game_progress' AND Name = 'gold') OR [Group] IN
                       ('economy', 'server.revisions', 'game_progress.unlocked_bullet_kinds', 'game_progress.unlocked_auxiliary_kinds'))",
                new { CreateUserId = _playerId, Ids = ids });
            if (protectedIds.Any()) { throw new BusinessArgumentException(ApiMessageKeys.EconomyGoldProtected); }
        }
    }
}
