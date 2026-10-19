using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ReconstructNodesServer.Auth
{
    public enum EmailChallengeIssueStatus { Issued, Throttled }

    public enum EmailChallengeVerifyStatus { Verified, Invalid, Expired, TooManyAttempts }

    public sealed record EmailChallengeIssue(EmailChallengeIssueStatus Status, string Code, int ExpiresInSeconds, int ResendAfterSeconds);

    /// <summary>
    /// Email 驗證碼挑戰（記憶體，與既有 Token 快取同為單一執行個體假設）。
    /// 每個信箱同時只有一個有效挑戰；驗證碼只保存加鹽雜湊、單次有效、限次嘗試，並綁定要求時的裝置 ID。
    /// 寄送節流：同信箱冷卻、同信箱與同裝置每小時上限，避免被當成垃圾信中繼或暴力猜碼。
    /// </summary>
    public sealed class EmailChallengeStore(IMemoryCache cache, TimeProvider clock)
    {
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
        public const int MaxAttempts = 5;
        public const int MaxSendsPerEmailPerWindow = 5;
        public const int MaxSendsPerDevicePerWindow = 10;
        public const int CodeDigits = 6;

        private readonly object _gate = new();

        private sealed class Challenge
        {
            public required byte[] Salt { get; init; }
            public required byte[] Hash { get; init; }
            public required string ClientId { get; init; }
            public required DateTimeOffset ExpiresAt { get; init; }
            public int Attempts { get; set; }
        }

        private sealed class SendCounter
        {
            public DateTimeOffset WindowStart { get; init; }
            public DateTimeOffset LastSent { get; set; }
            public int Count { get; set; }
        }

        /// <summary>email 必須已正規化（trim + 小寫）。</summary>
        public EmailChallengeIssue Issue(string email, string clientId)
        {
            lock (_gate)
            {
                DateTimeOffset now = clock.GetUtcNow();
                SendCounter emailCounter = GetCounter(EmailCounterKey(email), now);
                SendCounter deviceCounter = GetCounter(DeviceCounterKey(clientId), now);

                if (now - emailCounter.LastSent < ResendCooldown && emailCounter.Count > 0)
                {
                    int wait = (int)Math.Ceiling((ResendCooldown - (now - emailCounter.LastSent)).TotalSeconds);
                    return new EmailChallengeIssue(EmailChallengeIssueStatus.Throttled, string.Empty, 0, wait);
                }
                if (emailCounter.Count >= MaxSendsPerEmailPerWindow || deviceCounter.Count >= MaxSendsPerDevicePerWindow)
                {
                    int wait = (int)Math.Ceiling((emailCounter.WindowStart + SendWindow - now).TotalSeconds);
                    return new EmailChallengeIssue(EmailChallengeIssueStatus.Throttled, string.Empty, 0, Math.Max(wait, 1));
                }

                string code = NewCode();
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                cache.Set(ChallengeKey(email), new Challenge
                {
                    Salt = salt,
                    Hash = Hash(salt, code),
                    ClientId = clientId,
                    ExpiresAt = now + CodeLifetime,
                }, CodeLifetime);

                emailCounter.Count++;
                emailCounter.LastSent = now;
                deviceCounter.Count++;
                deviceCounter.LastSent = now;
                return new EmailChallengeIssue(EmailChallengeIssueStatus.Issued, code, (int)CodeLifetime.TotalSeconds, (int)ResendCooldown.TotalSeconds);
            }
        }

        /// <summary>寄信失敗時撤回挑戰與計數，讓玩家不必為沒寄出的信等冷卻。</summary>
        public void Revoke(string email, string clientId)
        {
            lock (_gate)
            {
                cache.Remove(ChallengeKey(email));
                if (cache.TryGetValue(EmailCounterKey(email), out SendCounter? emailCounter) && emailCounter!.Count > 0)
                {
                    emailCounter.Count--;
                    emailCounter.LastSent = DateTimeOffset.MinValue;
                }
                if (cache.TryGetValue(DeviceCounterKey(clientId), out SendCounter? deviceCounter) && deviceCounter!.Count > 0)
                {
                    deviceCounter.Count--;
                }
            }
        }

        public EmailChallengeVerifyStatus Verify(string email, string clientId, string code)
        {
            lock (_gate)
            {
                string key = ChallengeKey(email);
                if (!cache.TryGetValue(key, out Challenge? challenge) || challenge == null)
                {
                    return EmailChallengeVerifyStatus.Expired;
                }
                if (clock.GetUtcNow() >= challenge.ExpiresAt)
                {
                    cache.Remove(key);
                    return EmailChallengeVerifyStatus.Expired;
                }
                if (challenge.Attempts >= MaxAttempts)
                {
                    cache.Remove(key);
                    return EmailChallengeVerifyStatus.TooManyAttempts;
                }

                bool deviceMatches = FixedEquals(Encoding.UTF8.GetBytes(challenge.ClientId), Encoding.UTF8.GetBytes(clientId));
                bool codeMatches = FixedEquals(challenge.Hash, Hash(challenge.Salt, code));
                if (deviceMatches && codeMatches)
                {
                    cache.Remove(key);
                    return EmailChallengeVerifyStatus.Verified;
                }

                challenge.Attempts++;
                if (challenge.Attempts >= MaxAttempts)
                {
                    cache.Remove(key);
                    return EmailChallengeVerifyStatus.TooManyAttempts;
                }
                return EmailChallengeVerifyStatus.Invalid;
            }
        }

        private SendCounter GetCounter(string key, DateTimeOffset now)
        {
            if (cache.TryGetValue(key, out SendCounter? counter) && counter != null && now - counter.WindowStart < SendWindow)
            {
                return counter;
            }
            counter = new SendCounter { WindowStart = now, LastSent = DateTimeOffset.MinValue };
            cache.Set(key, counter, SendWindow);
            return counter;
        }

        private static string NewCode() =>
            RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeDigits)).ToString($"D{CodeDigits}");

        private static byte[] Hash(byte[] salt, string code)
        {
            byte[] codeBytes = Encoding.UTF8.GetBytes(code);
            byte[] input = new byte[salt.Length + codeBytes.Length];
            salt.CopyTo(input, 0);
            codeBytes.CopyTo(input, salt.Length);
            return SHA256.HashData(input);
        }

        private static bool FixedEquals(byte[] left, byte[] right) =>
            left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

        private static string ChallengeKey(string email) => "email-challenge:" + email;
        private static string EmailCounterKey(string email) => "email-send:" + email;
        private static string DeviceCounterKey(string clientId) => "email-send-device:" + clientId;
    }
}
