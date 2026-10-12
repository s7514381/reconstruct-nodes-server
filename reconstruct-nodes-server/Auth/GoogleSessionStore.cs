using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Library.Auth;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Auth
{
    /// <summary>
    /// Google sub → 獨立玩家的持久化端口。只處理 GoogleId 對應的玩家列，訪客列（ClientId + IsClientLogin）不在此讀寫。
    /// 以 BaseService 交易包住查找、建立與 Token 輪替；舊 Token 立即自快取移除，與訪客登入行為一致。
    /// </summary>
    public class GoogleSessionStore(PlayerService playerService, BaseService baseService, IMemoryCache cache) : IGoogleSessionStore
    {
        public async Task<GoogleSignInResult> SignInAsync(string googleSubject, string displayName, string locale, CancellationToken cancellationToken)
        {
            GoogleSignInResult? result = null;
            (bool isSuccess, Exception? exception) = await baseService.TransactionFunc(async () =>
            {
                Player player = await playerService.FindOrCreateGoogle(googleSubject, displayName);

                if (player.Token.HasValue) { cache.Remove(player.Token.Value); }
                player.Token = Guid.NewGuid();
                player.LastLoginDate = DateTime.Now;
                player.Locale = locale;
                await playerService.UpdateAsync(player);

                result = new GoogleSignInResult(player.Token.Value, player.Name ?? string.Empty);
            });

            if (!isSuccess || result == null)
            {
                throw new InvalidOperationException("Google 登入資料寫入失敗", exception);
            }
            return result;
        }
    }
}
