using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Auth;

namespace ReconstructNodesServer.Tests;

/// <summary>Email 驗證碼挑戰的離線測試：單次有效、綁定裝置、限次嘗試、過期與寄送節流。</summary>
[TestClass]
public class EmailChallengeStoreTests
{
    private const string Email = "player@example.com";
    private const string Device = "0123456789abcdef0123456789abcdef";
    private const string OtherDevice = "fedcba9876543210fedcba9876543210";

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (EmailChallengeStore Store, ManualClock Clock) Create()
    {
        var clock = new ManualClock();
        return (new EmailChallengeStore(new MemoryCache(new MemoryCacheOptions { Clock = new Clock(clock) }), clock), clock);
    }

    private sealed class Clock(TimeProvider provider) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => provider.GetUtcNow();
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    [TestMethod]
    public void Issue_ReturnsSixDigitCode_AndVerifyConsumesIt()
    {
        var (store, _) = Create();
        var issue = store.Issue(Email, Device);

        Assert.AreEqual(EmailChallengeIssueStatus.Issued, issue.Status);
        Assert.AreEqual(EmailChallengeStore.CodeDigits, issue.Code.Length);
        Assert.IsTrue(issue.Code.All(char.IsAsciiDigit));
        Assert.AreEqual(EmailChallengeVerifyStatus.Verified, store.Verify(Email, Device, issue.Code));
        Assert.AreEqual(EmailChallengeVerifyStatus.Expired, store.Verify(Email, Device, issue.Code), "驗證碼只能用一次");
    }

    [TestMethod]
    public void Verify_FromDifferentDevice_IsRejectedEvenWithCorrectCode()
    {
        var (store, _) = Create();
        var issue = store.Issue(Email, Device);

        Assert.AreEqual(EmailChallengeVerifyStatus.Invalid, store.Verify(Email, OtherDevice, issue.Code));
        Assert.AreEqual(EmailChallengeVerifyStatus.Verified, store.Verify(Email, Device, issue.Code));
    }

    [TestMethod]
    public void Verify_WrongCodeFiveTimes_LocksChallengeEvenForCorrectCode()
    {
        var (store, _) = Create();
        var issue = store.Issue(Email, Device);

        for (int i = 0; i < EmailChallengeStore.MaxAttempts - 1; i++)
        {
            Assert.AreEqual(EmailChallengeVerifyStatus.Invalid, store.Verify(Email, Device, WrongCode(issue.Code)));
        }
        Assert.AreEqual(EmailChallengeVerifyStatus.TooManyAttempts, store.Verify(Email, Device, WrongCode(issue.Code)));
        Assert.AreEqual(EmailChallengeVerifyStatus.Expired, store.Verify(Email, Device, issue.Code));
    }

    [TestMethod]
    public void Verify_AfterLifetime_IsExpired()
    {
        var (store, clock) = Create();
        var issue = store.Issue(Email, Device);

        clock.Now += EmailChallengeStore.CodeLifetime + TimeSpan.FromSeconds(1);
        Assert.AreEqual(EmailChallengeVerifyStatus.Expired, store.Verify(Email, Device, issue.Code));
    }

    [TestMethod]
    public void Issue_WithinCooldown_IsThrottled_ThenAllowedAndReplacesOldCode()
    {
        var (store, clock) = Create();
        var first = store.Issue(Email, Device);
        var early = store.Issue(Email, Device);

        Assert.AreEqual(EmailChallengeIssueStatus.Throttled, early.Status);
        Assert.IsTrue(early.ResendAfterSeconds is > 0 and <= 60);

        clock.Now += EmailChallengeStore.ResendCooldown;
        var second = store.Issue(Email, Device);
        Assert.AreEqual(EmailChallengeIssueStatus.Issued, second.Status);
        if (second.Code != first.Code)
        {
            Assert.AreEqual(EmailChallengeVerifyStatus.Invalid, store.Verify(Email, Device, first.Code), "重寄後舊碼失效");
        }
        Assert.AreEqual(EmailChallengeVerifyStatus.Verified, store.Verify(Email, Device, second.Code));
    }

    [TestMethod]
    public void Issue_PerEmailHourlyLimit_Throttles()
    {
        var (store, clock) = Create();
        for (int i = 0; i < EmailChallengeStore.MaxSendsPerEmailPerWindow; i++)
        {
            Assert.AreEqual(EmailChallengeIssueStatus.Issued, store.Issue(Email, Device).Status);
            clock.Now += EmailChallengeStore.ResendCooldown;
        }
        Assert.AreEqual(EmailChallengeIssueStatus.Throttled, store.Issue(Email, Device).Status);

        clock.Now += EmailChallengeStore.SendWindow;
        Assert.AreEqual(EmailChallengeIssueStatus.Issued, store.Issue(Email, Device).Status);
    }

    [TestMethod]
    public void Issue_PerDeviceHourlyLimit_ThrottlesAcrossDifferentEmails()
    {
        var (store, _) = Create();
        for (int i = 0; i < EmailChallengeStore.MaxSendsPerDevicePerWindow; i++)
        {
            Assert.AreEqual(EmailChallengeIssueStatus.Issued, store.Issue($"user{i}@example.com", Device).Status);
        }
        Assert.AreEqual(EmailChallengeIssueStatus.Throttled, store.Issue("another@example.com", Device).Status);
        Assert.AreEqual(EmailChallengeIssueStatus.Issued, store.Issue("another@example.com", OtherDevice).Status);
    }

    [TestMethod]
    public void Revoke_AfterSendFailure_InvalidatesCodeAndRemovesCooldown()
    {
        var (store, _) = Create();
        var issue = store.Issue(Email, Device);
        store.Revoke(Email, Device);

        Assert.AreEqual(EmailChallengeVerifyStatus.Expired, store.Verify(Email, Device, issue.Code));
        Assert.AreEqual(EmailChallengeIssueStatus.Issued, store.Issue(Email, Device).Status);
    }
}
