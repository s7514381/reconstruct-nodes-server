using System.Security.Cryptography;
using System.Text;
using Dapper;
using ReconstructNodesServer.Billing;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 看廣告領鑽石的離線測試：狀態推導、領取寫入批次內容、每日上限與 AdMob SSV 簽章檢查；
/// 唯一索引、並行與 Google 實際回呼需要真實資料庫與 AdMob，不在此涵蓋。
/// </summary>
[TestClass]
public class AdRewardCommandTests
{
    private static readonly DateTime Today = new(2026, 10, 21, 9, 0, 0);

    private static AdRewardGrant Verified(Guid? requestId = null) =>
        new() { TransactionId = Guid.NewGuid().ToString("N"), RequestId = requestId ?? Guid.NewGuid() };

    private static AdRewardGrant ClaimedRow(int slot) =>
        new() { TransactionId = Guid.NewGuid().ToString("N"), Claimed = true, DateKey = "2026-10-21", Slot = slot };

    private static AdRewardReceipt ReceiptOf(ReconstructNodesServer.Repositories.SqlBatchCommand command)
    {
        var parameters = (DynamicParameters)command.Parameters!;
        string json = parameters.Get<string>(parameters.ParameterNames.Single(name => name.StartsWith("Result")));
        return System.Text.Json.JsonSerializer.Deserialize<AdRewardReceipt>(json)!;
    }

    [TestMethod]
    public void Catalog_DefinesRewardAndDailyLimit()
    {
        Assert.AreEqual("diamond", AdRewardCatalog.DiamondItemKey);
        Assert.AreEqual(10, AdRewardCatalog.DiamondsPerAd);
        Assert.AreEqual(5, AdRewardCatalog.DailyLimit);
    }

    [TestMethod]
    public void Status_FreshDay_CanWatchWithoutPending()
    {
        var status = AdRewardCommand.BuildStatus(Today, new AdRewardCommand.State(), Guid.Empty);
        Assert.AreEqual("2026-10-21", status.Today);
        Assert.AreEqual(0, status.ClaimedToday);
        Assert.IsTrue(status.CanWatch);
        Assert.AreEqual("", status.PendingRequestId);
        Assert.AreEqual(10, status.Amount);
    }

    [TestMethod]
    public void Status_AtDailyLimit_CannotWatch()
    {
        var state = new AdRewardCommand.State { ClaimedToday = Enumerable.Range(1, 5).Select(ClaimedRow).ToList() };
        Assert.IsFalse(AdRewardCommand.BuildStatus(Today, state, Guid.Empty).CanWatch);
    }

    [TestMethod]
    public void Status_VerifiedUnclaimed_ReportsOldestPendingRequest()
    {
        var first = Verified();
        var state = new AdRewardCommand.State { Verified = [first, Verified()] };
        Assert.AreEqual(first.RequestId.ToString(), AdRewardCommand.BuildStatus(Today, state, Guid.Empty).PendingRequestId);
    }

    [TestMethod]
    public void Write_NextSlotRewardInOneBatch()
    {
        var grant = Verified();
        var state = new AdRewardCommand.State
        {
            Verified = [grant],
            ClaimedToday = [ClaimedRow(1), ClaimedRow(2)],
            Items = [new Item { ItemKey = "wood", Amount = 3 }],
        };
        var request = new AdRewardClaimRequest { RequestId = grant.RequestId };
        var command = AdRewardCommand.PrepareWrite(Guid.NewGuid(), request, "{}", state, Today);

        StringAssert.Contains(command.Sql, "BEGIN TRAN");
        StringAssert.Contains(command.Sql, "UPDATE t SET");
        StringAssert.Contains(command.Sql, "FROM [AdRewardGrant] t");
        StringAssert.Contains(command.Sql, "INSERT INTO [Item]");
        StringAssert.Contains(command.Sql, "COMMIT");
        Assert.IsTrue(grant.Claimed);
        Assert.AreEqual("2026-10-21", grant.DateKey);
        Assert.AreEqual(3, grant.Slot);
        var receipt = ReceiptOf(command);
        Assert.AreEqual(3, receipt.Slot);
        Assert.AreEqual("diamond", receipt.ItemId);
        Assert.AreEqual(10, receipt.Amount);
        Assert.AreEqual(10, receipt.Items.Single(i => i.ItemKey == "diamond").Amount);
        Assert.AreEqual(3, receipt.Items.Single(i => i.ItemKey == "wood").Amount);
    }

    [TestMethod]
    public void Write_ExistingDiamondRow_AddsRelativelyAndBackfillsReceipt()
    {
        var grant = Verified();
        var state = new AdRewardCommand.State
        {
            Verified = [grant],
            Items = [new Item { ItemKey = "diamond", Amount = 7 }],
        };
        var command = AdRewardCommand.PrepareWrite(Guid.NewGuid(), new AdRewardClaimRequest { RequestId = grant.RequestId }, "{}", state, Today);

        StringAssert.Contains(command.Sql, "UPDATE Item SET");
        Assert.IsFalse(command.Sql.Contains("INSERT INTO [Item]"));
        Assert.AreEqual(17, ReceiptOf(command).Items.Single(i => i.ItemKey == "diamond").Amount);
    }

    [TestMethod]
    public void Write_WithoutVerifiedRequest_IsPendingNotGranted()
    {
        var state = new AdRewardCommand.State { Verified = [Verified()] };
        var error = Assert.Throws<BusinessOperationException>(() =>
            AdRewardCommand.PrepareWrite(Guid.NewGuid(), new AdRewardClaimRequest { RequestId = Guid.NewGuid() }, "{}", state, Today));
        Assert.AreEqual(ApiMessageKeys.AdRewardPending, error.ApiMessage.Key);
    }

    [TestMethod]
    public void Write_AtDailyLimit_Rejected()
    {
        var grant = Verified();
        var state = new AdRewardCommand.State { Verified = [grant], ClaimedToday = Enumerable.Range(1, 5).Select(ClaimedRow).ToList() };
        var error = Assert.Throws<BusinessOperationException>(() =>
            AdRewardCommand.PrepareWrite(Guid.NewGuid(), new AdRewardClaimRequest { RequestId = grant.RequestId }, "{}", state, Today));
        Assert.AreEqual(ApiMessageKeys.AdRewardLimitReached, error.ApiMessage.Key);
        Assert.IsFalse(grant.Claimed);
    }

    // ---- AdMob SSV 簽章 ----

    private static (ECDsa Key, byte[] PublicKey) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, key.ExportSubjectPublicKeyInfo());
    }

    private static string SignedQuery(ECDsa key, string content, string keyId)
    {
        byte[] signature = key.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        string encoded = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"?{content}&signature={encoded}&key_id={keyId}";
    }

    private const string Content = "ad_network=5450213213286189855&ad_unit=1234&custom_data=abc&reward_amount=1&reward_item=diamond&timestamp=1&transaction_id=T1&user_id=U1";

    [TestMethod]
    public void Signature_ValidQuery_Verifies()
    {
        var (key, publicKey) = NewKey();
        using (key)
        {
            var signed = AdMobSsvSignature.Parse(SignedQuery(key, Content, "3335741209"));
            Assert.IsNotNull(signed);
            Assert.AreEqual(Content, signed.Content);
            Assert.AreEqual("3335741209", signed.KeyId);
            Assert.IsTrue(AdMobSsvSignature.Verify(publicKey, signed));
        }
    }

    [TestMethod]
    public void Signature_TamperedContent_Rejected()
    {
        var (key, publicKey) = NewKey();
        using (key)
        {
            var signed = AdMobSsvSignature.Parse(SignedQuery(key, Content, "1"))!;
            Assert.IsFalse(AdMobSsvSignature.Verify(publicKey, signed with { Content = Content.Replace("reward_amount=1", "reward_amount=999") }));
        }
    }

    [TestMethod]
    public void Signature_OtherKey_Rejected()
    {
        var (key, _) = NewKey();
        var (other, otherPublic) = NewKey();
        using (key)
        using (other)
        {
            var signed = AdMobSsvSignature.Parse(SignedQuery(key, Content, "1"))!;
            Assert.IsFalse(AdMobSsvSignature.Verify(otherPublic, signed));
        }
    }

    [TestMethod]
    public void Signature_MalformedQuery_ParsesToNull()
    {
        Assert.IsNull(AdMobSsvSignature.Parse("?user_id=U1"));
        Assert.IsNull(AdMobSsvSignature.Parse("?a=b&key_id=1&signature=AAAA"));
        Assert.IsNull(AdMobSsvSignature.Parse("?a=b&signature=%%%&key_id=1"));
        Assert.IsNull(AdMobSsvSignature.Parse("?a=b&signature=AAAA&key_id=1&extra=2"));
    }

    [TestMethod]
    public void Keys_ParsedByKeyId()
    {
        var (key, publicKey) = NewKey();
        using (key)
        {
            string json = $$"""{"keys":[{"keyId":3335741209,"pem":"x","base64":"{{Convert.ToBase64String(publicKey)}}"}]}""";
            var keys = AdMobSsvKeySource.ParseKeys(json);
            CollectionAssert.AreEqual(publicKey, keys["3335741209"]);
        }
    }
}
