
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models.Enum;
using ReconstructNodesServer.Argument;
using System.Data.SqlClient;

namespace ReconstructNodesServer.Services
{
    public class PlayerService(BaseServiceArgument argument) : _ModelService<Player, PlayerModel>(argument)
    {
        public Guid? PlayerId { get; set; }
        public Player? Player { get; set; }

        public async Task<Guid?> GetIdByToken(Guid token) 
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
        public async Task<Player> FindOrCreateGuest(string clientId)
        {
            Player? player = await GetDataByLogin(new LoginRequest { Type = ELoginType.訪客, ClientId = clientId });
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

        /// <summary>
        /// Google 登入：以已通過 ID Token 驗證的 sub 找到或建立獨立玩家。
        /// 只查 GoogleId，不讀寫訪客列（IsClientLogin），因此不會遷移或覆寫訪客進度。
        /// 併發首次登入時，唯一索引 UX_Player_GoogleId_NonClient 會讓後到的 INSERT 失敗（2601/2627），
        /// 此時改讀取已提交的同一列，確保同一 sub 只會對應一筆玩家。
        /// </summary>
        public async Task<Player> FindOrCreateGoogle(string googleSubject, string displayName)
        {
            Player? player = await GetDataByLogin(new LoginRequest { Type = ELoginType.Google, GoogleId = googleSubject });
            if (player != null) { return player; }

            player = new Player()
            {
                Name = displayName,
                Token = Guid.NewGuid(),
                GoogleId = googleSubject,
                ClientId = string.Empty,
                IsClientLogin = false,
            };
            try
            {
                await InsertAsync(player);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // 唯一鍵衝突：另一請求已建立同一 GoogleId 的玩家；語句級錯誤不會中止外層交易，可在同一交易內重讀
                Player? existing = await GetDataByLogin(new LoginRequest { Type = ELoginType.Google, GoogleId = googleSubject });
                return existing ?? throw new InvalidOperationException("Google 玩家唯一鍵衝突但查無既有列", ex);
            }
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

        public async Task<Player?> GetDataByLogin(LoginRequest model)
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
                    // 只比對非訪客列：舊版 Bind 可能把 GoogleId 寫到訪客列，Google 登入不得接管該訪客進度。
                    subWhere += " and GoogleId = @GoogleId and IsClientLogin = 0 ";
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

    }
}
