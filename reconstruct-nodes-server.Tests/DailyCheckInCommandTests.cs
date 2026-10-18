using Dapper;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 每日簽到的離線測試：獎勵目錄、狀態推導與領取寫入批次內容；
/// 唯一索引與並行行為需要真實資料庫，不在此涵蓋。
/// </summary>
[TestClass]
public class DailyCheckInCommandTests
{
    private static readonly DateTime Today = new(2026, 10, 18, 9, 0, 0);

    private static DailyCheckInClaimRequest Request() => new() { RequestId = Guid.NewGuid() };

    private static DailyCheckIn Row(string date, int ordinal) => new() { DateKey = date, Ordinal = ordinal };

    private static DailyCheckInReceipt ReceiptOf(ReconstructNodesServer.Repositories.SqlBatchCommand command)
    {
        var parameters = (DynamicParameters)command.Parameters!;
        string json = parameters.Get<string>(parameters.ParameterNames.Single(name => name.StartsWith("Result")));
        return System.Text.Json.JsonSerializer.Deserialize<DailyCheckInReceipt>(json)!;
    }

    [TestMethod]
    public void Catalog_HasOneRewardPerRequiredDay()
    {
        Assert.AreEqual(20, DailyCheckInCatalog.All.Count);
        Assert.IsTrue(DailyCheckInCatalog.All.All(reward => reward.Amount > 0));
        Assert.AreEqual(DailyCheckInCatalog.EnergyExtractorItemKey, DailyCheckInCatalog.RewardFor(5).ItemKey);
        Assert.AreEqual(100, DailyCheckInCatalog.RewardFor(20).Amount);
    }

    [TestMethod]
    public void Status_EmptyMonth_CanClaimAndListsAllRewards()
    {
        var status = DailyCheckInCommand.BuildStatus(Today, []);
        Assert.AreEqual("2026-10", status.Month);
        Assert.AreEqual("2026-10-18", status.Today);
        Assert.AreEqual(20, status.Rewards.Count);
        Assert.IsTrue(status.CanClaim);
    }

    [TestMethod]
    public void Status_ClaimedToday_CannotClaim()
    {
        var status = DailyCheckInCommand.BuildStatus(Today, [Row("2026-10-18", 1)]);
        Assert.IsFalse(status.CanClaim);
        Assert.AreEqual(1, status.Claimed.Count);
    }

    [TestMethod]
    public void Status_MonthComplete_CannotClaimOnNewDay()
    {
        var month = Enumerable.Range(1, 20).Select(i => Row($"2026-10-{i:00}", i)).ToList();
        var status = DailyCheckInCommand.BuildStatus(new DateTime(2026, 10, 25), month);
        Assert.IsFalse(status.CanClaim);
    }

    [TestMethod]
    public void Write_NextOrdinalRewardInOneBatch()
    {
        var state = new DailyCheckInCommand.State
        {
            Month = [Row("2026-10-16", 1), Row("2026-10-17", 2)],
            Items = [new Item { ItemKey = "wood", Amount = 3 }],
        };
        var command = DailyCheckInCommand.PrepareWrite(Guid.NewGuid(), Request(), "{}", state, Today);

        StringAssert.Contains(command.Sql, "BEGIN TRAN");
        StringAssert.Contains(command.Sql, "INSERT INTO [DailyCheckIn]");
        StringAssert.Contains(command.Sql, "INSERT INTO [Item]");
        StringAssert.Contains(command.Sql, "COMMIT");
        var receipt = ReceiptOf(command);
        var expected = DailyCheckInCatalog.RewardFor(3);
        Assert.AreEqual(3, receipt.Ordinal);
        Assert.AreEqual("2026-10-18", receipt.Date);
        Assert.AreEqual(expected.ItemKey, receipt.ItemId);
        Assert.AreEqual(expected.Amount, receipt.Items.Single(i => i.ItemKey == expected.ItemKey).Amount);
        Assert.AreEqual(3, receipt.Items.Single(i => i.ItemKey == "wood").Amount);
    }

    [TestMethod]
    public void Write_ExistingRewardRow_AddsRelativelyAndBackfillsReceipt()
    {
        var reward = DailyCheckInCatalog.RewardFor(1);
        var state = new DailyCheckInCommand.State { Items = [new Item { ItemKey = reward.ItemKey, Amount = 7 }] };
        var command = DailyCheckInCommand.PrepareWrite(Guid.NewGuid(), Request(), "{}", state, Today);

        StringAssert.Contains(command.Sql, "UPDATE Item SET");
        Assert.IsFalse(command.Sql.Contains("INSERT INTO [Item]"));
        Assert.AreEqual(7 + reward.Amount, ReceiptOf(command).Items.Single(i => i.ItemKey == reward.ItemKey).Amount);
    }

    [TestMethod]
    public void Write_AlreadyClaimedToday_Rejected()
    {
        var state = new DailyCheckInCommand.State { Month = [Row("2026-10-18", 1)] };
        Assert.Throws<BusinessOperationException>(() =>
            DailyCheckInCommand.PrepareWrite(Guid.NewGuid(), Request(), "{}", state, Today));
    }

    [TestMethod]
    public void Write_MonthComplete_Rejected()
    {
        var state = new DailyCheckInCommand.State { Month = Enumerable.Range(1, 20).Select(i => Row($"2026-10-{i:00}", i)).ToList() };
        Assert.Throws<BusinessOperationException>(() =>
            DailyCheckInCommand.PrepareWrite(Guid.NewGuid(), Request(), "{}", state, new DateTime(2026, 10, 25)));
    }
}
