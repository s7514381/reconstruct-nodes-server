using Dapper;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 挑戰關卡的離線測試：今日狀態推導與通關獎勵的寫入批次內容；
/// 並行領取需要真實資料庫，不在此涵蓋。進入時扣次數見 BatchCommandTests 的挑戰案例。
/// </summary>
[TestClass]
public class ChallengeCommandTests
{
    private static readonly DateTime Today = new(2026, 10, 20, 9, 0, 0);

    private static PlayerParams Row(string group, string name, string value) => new() { Group = group, Name = name, Value = value };

    private static PlayerParams Attempts(int used, bool rewarded, string date = "2026-10-20") =>
        Row(ChallengeRules.Group, ChallengeRules.AttemptName(5),
            new ChallengeRules.Attempts { Date = date, Used = used, Rewarded = rewarded }.Serialize());

    private static ChallengeCommand.State Cleared(PlayerParams attempts, params Item[] items) => new()
    {
        Parameters =
        [
            Row("game_progress", "difficulty", "5"), Row("game_progress", "wave", "10"),
            Row("game_progress", "wave_completed", "True"), attempts,
        ],
        Items = [.. items],
    };

    private static SqlBatchCommand Write(ChallengeCommand.State state) =>
        ChallengeCommand.PrepareWrite(Guid.NewGuid(), new ChallengeClaimRequest { RequestId = Guid.NewGuid() }, "{}", state, Today);

    private static ChallengeRewardReceipt ReceiptOf(SqlBatchCommand command)
    {
        var parameters = (DynamicParameters)command.Parameters!;
        string json = parameters.Get<string>(parameters.ParameterNames.Single(name => name.StartsWith("Result")));
        return System.Text.Json.JsonSerializer.Deserialize<ChallengeRewardReceipt>(json)!;
    }

    [TestMethod]
    public void Rules_AreThreeAttemptsAndThreeExtractors()
    {
        Assert.AreEqual(3, ChallengeRules.DailyLimit);
        Assert.AreEqual("energy_extractor", ChallengeRules.RewardItemKey);
        Assert.AreEqual(3, ChallengeRules.RewardAmount);
        CollectionAssert.AreEqual(new[] { 5 }, ChallengeRules.Difficulties.ToArray());
    }

    [TestMethod]
    public void Status_NoRowIsFullAndOldDayResets()
    {
        var empty = ChallengeRules.BuildStatus([], "2026-10-20").Stages.Single();
        Assert.AreEqual(5, empty.Difficulty);
        Assert.AreEqual(3, empty.Remaining);
        Assert.AreEqual(3, empty.DailyLimit);
        Assert.AreEqual(3, empty.RewardAmount);

        var used = ChallengeRules.BuildStatus([Attempts(2, false)], "2026-10-20").Stages.Single();
        Assert.AreEqual(1, used.Remaining);

        var nextDay = ChallengeRules.BuildStatus([Attempts(3, true, "2026-10-19")], "2026-10-20").Stages.Single();
        Assert.AreEqual(3, nextDay.Remaining);
    }

    [TestMethod]
    public void Write_ClearedChallengeGrantsThreeExtractorsAndMarksRewardedInOneBatch()
    {
        var command = Write(Cleared(Attempts(1, false), new Item { ItemKey = "wood", Amount = 3 }));

        StringAssert.Contains(command.Sql, "BEGIN TRAN");
        StringAssert.Contains(command.Sql, "UPDATE PlayerParams SET Value");
        StringAssert.Contains(command.Sql, "INSERT INTO [Item]");
        StringAssert.Contains(command.Sql, "COMMIT");
        var receipt = ReceiptOf(command);
        Assert.AreEqual(5, receipt.Difficulty);
        Assert.AreEqual("2026-10-20", receipt.Date);
        Assert.AreEqual(3, receipt.Amount);
        Assert.AreEqual(3, receipt.Items.Single(i => i.ItemKey == "energy_extractor").Amount);
        Assert.AreEqual(3, receipt.Items.Single(i => i.ItemKey == "wood").Amount);
    }

    [TestMethod]
    public void Write_ExistingExtractorRow_AddsRelativelyAndBackfillsReceipt()
    {
        var command = Write(Cleared(Attempts(2, false), new Item { ItemKey = "energy_extractor", Amount = 4 }));

        StringAssert.Contains(command.Sql, "UPDATE Item SET");
        Assert.IsFalse(command.Sql.Contains("INSERT INTO [Item]"));
        Assert.AreEqual(7, ReceiptOf(command).Items.Single(i => i.ItemKey == "energy_extractor").Amount);
    }

    [TestMethod]
    public void Write_AlreadyRewardedEntry_Rejected()
    {
        var error = Assert.ThrowsExactly<BusinessOperationException>(() => Write(Cleared(Attempts(1, true))));
        Assert.AreEqual(ApiMessageKeys.ChallengeAlreadyRewarded, error.ApiMessage.Key);
    }

    [TestMethod]
    public void Write_RewardedFlagFromYesterdayStillBlocksAfterMidnight()
    {
        Assert.Throws<BusinessOperationException>(() => Write(Cleared(Attempts(1, true, "2026-10-19"))));
    }

    [TestMethod]
    public void Write_NotClearedOrNotInChallenge_Rejected()
    {
        var unfinished = Cleared(Attempts(1, false));
        unfinished.Parameters.Single(p => p.Name == "wave").Value = "9";
        var error = Assert.ThrowsExactly<BusinessOperationException>(() => Write(unfinished));
        Assert.AreEqual(ApiMessageKeys.ChallengeNotCleared, error.ApiMessage.Key);

        var notCompleted = Cleared(Attempts(1, false));
        notCompleted.Parameters.Single(p => p.Name == "wave_completed").Value = "False";
        Assert.Throws<BusinessOperationException>(() => Write(notCompleted));

        var trial = Cleared(Attempts(1, false));
        trial.Parameters.Single(p => p.Name == "difficulty").Value = "4";
        Assert.Throws<BusinessOperationException>(() => Write(trial));
    }
}
