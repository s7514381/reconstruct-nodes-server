using Dapper;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// Google Play 內購的離線測試：驗證商品目錄與發放寫入批次內容（單一原子批次、購買紀錄、鑽石與回條）；
/// Google 驗證、token 唯一索引與並行行為需要真實服務，不在此涵蓋。
/// </summary>
[TestClass]
public class PurchaseCommandTests
{
    private static GooglePlayPurchaseRequest Request() => new()
    {
        RequestId = Guid.NewGuid(), ProductId = PurchaseCatalog.Diamond100, PurchaseToken = "token-1",
    };


    private static GooglePlayPurchaseReceipt ReceiptOf(ReconstructNodesServer.Repositories.SqlBatchCommand command)
    {
        var parameters = (DynamicParameters)command.Parameters!;
        string json = parameters.Get<string>(parameters.ParameterNames.Single(name => name.StartsWith("Result")));
        return System.Text.Json.JsonSerializer.Deserialize<GooglePlayPurchaseReceipt>(json)!;
    }

    [TestMethod]
    public void Catalog_Diamond100_Grants100()
    {
        Assert.IsTrue(PurchaseCatalog.TryGetDiamonds("diamond_100", out int diamonds));
        Assert.AreEqual(100, diamonds);
        Assert.IsFalse(PurchaseCatalog.TryGetDiamonds("diamond_999", out _));
    }

    [TestMethod]
    public void Write_NoDiamondRow_InsertsPurchaseAndRewardInOneBatch()
    {
        var request = Request();
        var state = new PurchaseCommand.State { Items = [new Item { ItemKey = "wood", Amount = 3 }] };
        var command = PurchaseCommand.PrepareWrite(Guid.NewGuid(), request, "{}", state, 100, "GPA.1", out Guid id);

        StringAssert.Contains(command.Sql, "BEGIN TRAN");
        StringAssert.Contains(command.Sql, "INSERT INTO [IapPurchase]");
        StringAssert.Contains(command.Sql, "INSERT INTO [Item]");
        StringAssert.Contains(command.Sql, "COMMIT");
        Assert.AreNotEqual(Guid.Empty, id);
        var receipt = ReceiptOf(command);
        Assert.AreEqual(100, receipt.Diamonds);
        Assert.AreEqual(100, receipt.Items.Single(i => i.ItemKey == "diamond").Amount);
        Assert.AreEqual(3, receipt.Items.Single(i => i.ItemKey == "wood").Amount);
    }

    [TestMethod]
    public void Write_ExistingDiamondRow_AddsRelativelyAndBackfillsReceipt()
    {
        var request = Request();
        var state = new PurchaseCommand.State { Items = [new Item { ItemKey = "diamond", Amount = 7 }] };
        var command = PurchaseCommand.PrepareWrite(Guid.NewGuid(), request, "{}", state, 100, "GPA.1", out _);

        StringAssert.Contains(command.Sql, "UPDATE Item SET @Amount");
        StringAssert.Contains(command.Sql, "JSON_MODIFY");
        Assert.IsFalse(command.Sql.Contains("INSERT INTO [Item]"));
        Assert.AreEqual(107, ReceiptOf(command).Items.Single().Amount);
    }
}
