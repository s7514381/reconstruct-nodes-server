
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models.Enum;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class PlayerService(BaseServiceArgument argument) : _ModelService<Player, PlayerModel>(argument)
    {
        public Guid? PlayerId { get; set; }
        public Player? Player { get; set; }

        public async Task<Guid?> GetPlayerIdByToken(Guid token) 
        {
            string sql = $@"select top 1 Id 
                              from Player
                             where Token = @Token";
            var result = await QueryFirstAsync<Guid?>(sql, new { Token = token });
            return result;
        }

        public async Task<Player?> GetDataByToken(Guid token)
        {
            string sql = $@"select top 1 *
                              from Player
                             where Token = @Token";
            var result = await QueryFirstAsync<Player>(sql, new { Token = token });
            return result;
        }

        public async Task<Player?> GetDataByAccount(string account)
        {
            string sql = $@"select top 1 *
                              from Player
                             where Account = @Account";
            var result = await QueryFirstAsync<Player>(sql, new { Account = account });
            return result;
        }

        /// <summary>訪客登入：依 ClientId（前端 device ID）找回既有訪客帳號，找不到則建立新帳號。</summary>
        public async Task<Player> FindOrCreateGuestPlayer(string clientId)
        {
            Player? player = await GetPlayerByLogin(new LoginRequest { Type = ELoginType.訪客, ClientId = clientId });
            if (player != null) { return player; }

            player = new Player()
            {
                Name = string.Empty,
                Token = Guid.NewGuid(),
                ClientId = clientId,
                IsClientLogin = true,
            };
            await InsertAsync(player);
            return player;
        }

        public async Task<Player?> GetDataByEmail(string email)
        {
            string sql = $@"select top 1 *
                              from Player
                             where Email = @Email";
            var result = await QueryFirstAsync<Player>(sql, new { Email = email });
            return result;
        }

        protected override async Task SetModel(PlayerModel viewModel)
        {
            await base.SetModel(viewModel);

            viewModel.SettingVersionList = (await _lazy.SettingVersionService.Value.GetModelList()).ToList();
        }

        protected override async Task ModelSave(PlayerModel viewModel)
        {
            await base.ModelSave(viewModel);

            await _lazy.SettingVersionService.Value.UpdateDetail(viewModel, viewModel.SettingVersionList);
        }

        public async Task<PlayerCompleteModel?> GetCompleteModel() 
        {
            List<Task> tasks = new List<Task>();
            PlayerCompleteModel model = await QueryFirstAsync<PlayerCompleteModel>(SqlData(), new { Id = _playerId });
            if (model == null) { return null; }

            tasks.AddTask(async () => { await SetModel(model); });
            tasks.AddTask(async () => { model.MapList = (await _lazy.MapService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.AchievementList = (await _lazy.AchievementService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.EquipmentList = (await _lazy.EquipmentService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.AbilityList = (await _lazy.AbilityService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.CharacterList = (await _lazy.CharacterService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.AbilitySelectList = (await _lazy.AbilitySelectService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.GameCollectionList = (await _lazy.GameCollectionService.Value.GetModelList()).ToList(); });
            tasks.AddTask(async () => { model.PlayerParamsList = (await _lazy.PlayerParamsService.Value.GetModelList()).ToList(); });
            await Task.WhenAll(tasks);

            return model;
        }

        public async Task<Player?> GetPlayerByLogin(LoginRequest model)
        {
            string subWhere = " where 1=1 ";

            switch (model.Type)
            {
                case ELoginType.訪客:
                    subWhere += $@" and ClientId = @ClientId 
                                    and IsClientLogin = 1
                                  ";
                    break;
                case ELoginType.Google:
                    subWhere += " and GoogleId = @GoogleId ";
                    break;
                case ELoginType.Email:
                    subWhere += " and Email = @Email ";
                    break;
                default:
                    subWhere = " where 1=0 ";
                    break;
            }
            string sql = $@"select top 1 * 
                              from Player
                           {subWhere}";
            var result = await QueryFirstAsync<Player?>(sql, model);
            return result;
        }

        public async Task<bool> EmailVerification(string email, string verificationCode)
        {
            string sql = $@"select count(*)
                              from Player
                             where EmailVerify = @EmailVerify
                               and EmailVerificationCode = @EmailVerificationCode";
            var count = await QueryFirstAsync<int>(sql, new { EmailVerify = email, EmailVerificationCode = verificationCode });
            return count > 0;
        }

        public async Task SendEmail(string email, string verificationCode)
        {
            await _mailSender.SentAsync(email,
                "【創生戰記】Email驗證碼",
                $"<p>親愛的使用者您好：</p><p>Email驗證碼為:{verificationCode}</p>"
                );
        }

        public async Task<Player?> PlusSlot(int count)
        {
            string sql = $@"UPDATE Player
                               SET InventorySlotCount = ISNULL(InventorySlotCount, 0) + @Count
                            OUTPUT INSERTED.*
                             WHERE Id = @Id";
            var result = await QueryFirstAsync<Player?>(sql, new { Id = _playerId,  Count = count });
            return result;
        }


    }
}
