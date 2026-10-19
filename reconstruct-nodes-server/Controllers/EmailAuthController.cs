using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Auth;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>
    /// Email 登入：輸入信箱 → 寄驗證碼 → 輸入驗證碼 → 取得 session。
    /// 登入一律帶裝置 ID（ClientId）：帳號綁定的可信裝置再次登入可直接取得 session；
    /// 新帳號或換裝置（ClientId 不同）必須重新收信驗證，驗證成功後可信裝置改為目前裝置。
    /// </summary>
    public class EmailAuthController(
        BaseApiArgument argument,
        PlayerService playerService,
        EmailChallengeStore challenges,
        IMemoryCache cache
        ) : BaseApiController(argument)
    {
        private const int MaxEmailLength = 254;
        private const int MaxClientIdLength = 64;
        private const int MinClientIdLength = 16;
        private const int MaxDisplayNameLength = 32;

        /// <summary>
        /// 以信箱＋裝置 ID 登入。可信裝置直接回傳 Token；否則寄出驗證碼並回傳 VerificationRequired。
        /// 重複呼叫即「重寄」，受冷卻與每小時上限節流。
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Login(EmailLoginRequest request)
        {
            if (!TryNormalize(request.Email, request.ClientId, out string email, out string clientId))
            {
                return Failure(400, request.ClientId == null || !IsValidClientId(request.ClientId.Trim())
                    ? ApiMessageKeys.AuthDeviceRequired : ApiMessageKeys.AuthEmailInvalid);
            }

            try
            {
                Player? existing = await playerService.FindEmailPlayer(email);
                if (existing != null && IsTrustedDevice(existing, clientId))
                {
                    return await IssueSession(email, clientId, request.Locale, requireVerified: false);
                }

                EmailChallengeIssue issue = challenges.Issue(email, clientId);
                if (issue.Status == EmailChallengeIssueStatus.Throttled)
                {
                    return StatusCode(429, BackendApiResult<EmailLoginData>.Create(429,
                        new ApiMessage(ApiMessageKeys.AuthEmailRateLimited, new Dictionary<string, string>
                        {
                            ["seconds"] = issue.ResendAfterSeconds.ToString(CultureInfo.InvariantCulture),
                        })));
                }

                try
                {
                    await _mailSender.SendOrThrowAsync(email, null, MailSubject(request.Locale), MailBody(request.Locale, issue.Code));
                }
                catch (Exception ex)
                {
                    challenges.Revoke(email, clientId);
                    _logger.LogError(ex, "Email 驗證信寄送失敗");
                    return Failure(503, ApiMessageKeys.AuthEmailUnavailable);
                }

                return Ok(BackendApiResult<EmailLoginData>.Create(200, string.Empty, new EmailLoginData
                {
                    VerificationRequired = true,
                    ExpiresInSeconds = issue.ExpiresInSeconds,
                    ResendAfterSeconds = issue.ResendAfterSeconds,
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email 登入發生未預期例外");
                return Failure(500, ApiMessageKeys.AuthLoginFailed);
            }
        }

        /// <summary>驗證信箱驗證碼；成功才建立／取得玩家、綁定目前裝置並簽發 Token。</summary>
        [HttpPost]
        public async Task<IActionResult> Verify(EmailVerifyRequest request)
        {
            if (!TryNormalize(request.Email, request.ClientId, out string email, out string clientId))
            {
                return Failure(400, request.ClientId == null || !IsValidClientId(request.ClientId.Trim())
                    ? ApiMessageKeys.AuthDeviceRequired : ApiMessageKeys.AuthEmailInvalid);
            }
            string code = request.Code?.Trim() ?? string.Empty;
            if (code.Length != EmailChallengeStore.CodeDigits || !code.All(char.IsAsciiDigit))
            {
                return Failure(400, ApiMessageKeys.AuthEmailCodeInvalid);
            }

            try
            {
                switch (challenges.Verify(email, clientId, code))
                {
                    case EmailChallengeVerifyStatus.Verified:
                        return await IssueSession(email, clientId, request.Locale, requireVerified: true);
                    case EmailChallengeVerifyStatus.Expired:
                        return Failure(401, ApiMessageKeys.AuthEmailCodeExpired);
                    case EmailChallengeVerifyStatus.TooManyAttempts:
                        return Failure(429, ApiMessageKeys.AuthEmailTooManyAttempts);
                    default:
                        return Failure(401, ApiMessageKeys.AuthEmailCodeInvalid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email 驗證發生未預期例外");
                return Failure(500, ApiMessageKeys.AuthLoginFailed);
            }
        }

        private async Task<IActionResult> IssueSession(string email, string clientId, string? locale, bool requireVerified)
        {
            EmailLoginData? data = null;
            (bool isSuccess, ApiMessage? error) = await TransactionFunc(async () =>
            {
                Player player = requireVerified
                    ? await playerService.FindOrCreateEmail(email, clientId, DisplayNameOf(email))
                    : await playerService.FindEmailPlayer(email)
                        ?? throw new InvalidOperationException("Email 玩家在可信裝置登入期間消失");

                // 與 Device／Google 登入相同：每次登入重置 Token，舊 Token 立即失效。
                if (player.Token.HasValue) { cache.Remove(player.Token); }
                player.Token = Guid.NewGuid();
                player.LastLoginDate = DateTime.Now;
                player.Locale = locale ?? string.Empty;
                await playerService.UpdateAsync(player);

                data = new EmailLoginData { Token = player.Token.Value, Name = player.Name ?? string.Empty };
            });

            if (!isSuccess)
            {
                _logger.LogError("Email 登入失敗: {Error}", error);
                return TransactionFailure(error);
            }
            return Ok(BackendApiResult<EmailLoginData>.Create(200, string.Empty,
                data ?? throw new InvalidOperationException("Email login transaction completed without data")));
        }

        private static IActionResult Failure(int status, string key) =>
            new ObjectResult(BackendApiResult<EmailLoginData>.Create(status, key)) { StatusCode = status };

        private static bool IsTrustedDevice(Player player, string clientId)
        {
            if (string.IsNullOrEmpty(player.ClientId)) { return false; }
            byte[] stored = Encoding.UTF8.GetBytes(player.ClientId);
            byte[] supplied = Encoding.UTF8.GetBytes(clientId);
            return stored.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(stored, supplied);
        }

        private static bool TryNormalize(string? rawEmail, string? rawClientId, out string email, out string clientId)
        {
            email = string.Empty;
            clientId = rawClientId?.Trim() ?? string.Empty;
            if (!IsValidClientId(clientId)) { return false; }

            string candidate = rawEmail?.Trim().Normalize().ToLowerInvariant() ?? string.Empty;
            if (candidate.Length == 0 || candidate.Length > MaxEmailLength
                || !ReconstructNodesServer.Utility.EmailUtility.Validate(candidate)
                || !MailAddress.TryCreate(candidate, out MailAddress? address) || address.Address != candidate)
            {
                return false;
            }
            email = candidate;
            return true;
        }

        private static bool IsValidClientId(string value) =>
            value.Length >= MinClientIdLength && value.Length <= MaxClientIdLength
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

        private static string DisplayNameOf(string email)
        {
            string local = email[..email.IndexOf('@')];
            return local.Length <= MaxDisplayNameLength ? local : local[..MaxDisplayNameLength];
        }

        private static bool IsChinese(string? locale) =>
            locale != null && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        private static string MailSubject(string? locale) =>
            IsChinese(locale) ? "【創生戰記】登入驗證碼" : "[Reconstruct Nodes] Sign-in verification code";

        private static string MailBody(string? locale, string code) => IsChinese(locale)
            ? $"<p>親愛的玩家您好：</p><p>您的登入驗證碼為：<strong style=\"font-size:20px;letter-spacing:4px\">{code}</strong></p>"
              + "<p>驗證碼 10 分鐘內有效。若這不是您本人的操作，請忽略此信。</p>"
            : $"<p>Hello,</p><p>Your sign-in verification code is: <strong style=\"font-size:20px;letter-spacing:4px\">{code}</strong></p>"
              + "<p>The code is valid for 10 minutes. If you did not request it, please ignore this email.</p>";
    }

    public class EmailLoginRequest
    {
        public string? Email { get; set; }
        public string? ClientId { get; set; }
        public string? Locale { get; set; }
    }

    public class EmailVerifyRequest
    {
        public string? Email { get; set; }
        public string? ClientId { get; set; }
        public string? Code { get; set; }
        public string? Locale { get; set; }
    }

    /// <summary>VerificationRequired 為 true 時只含倒數資訊（尚未登入）；否則含 Token 與 Name。</summary>
    public class EmailLoginData
    {
        public bool VerificationRequired { get; set; }
        public Guid? Token { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ExpiresInSeconds { get; set; }
        public int ResendAfterSeconds { get; set; }
    }
}
