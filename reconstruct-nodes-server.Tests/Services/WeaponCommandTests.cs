using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Controllers;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services;

/// <summary>In-memory contract/ledger tests; SQL rollback and isolation remain opt-in.</summary>
[TestClass]
public class WeaponCommandTests
{
    [TestMethod]
    public void Routes_ExposeBusinessActionsWithoutGenericWeaponWrites()
    {
        foreach (string action in new[] { "Acquire", "Refine", "Disassemble", "GetInstanceList" })
        {
            Assert.IsNotNull(typeof(WeaponController).GetMethod(action)!
                .GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
        }
        Assert.IsNull(typeof(WeaponController).GetMethod("Save"));
        Assert.IsNull(typeof(WeaponController).GetMethod("Delete"));
        Assert.IsNotNull(typeof(CharacterController).GetMethod("EquipWeapon"));
        foreach (string action in new[] { "EquipWeapon", "RefineWeapon", "DisassembleWeapons" })
        { Assert.IsNull(typeof(ForegroundController).GetMethod(action)); }
        Assert.IsNotNull(typeof(ForegroundController).GetMethod("ConfigureSkills"));
    }

    [TestMethod]
    public void Requests_HaveStableIdentityAndNoCheckpointOrSchedulingFlag()
    {
        CommandRequest[] requests = [
            new WeaponAcquireRequest(), new WeaponRefineRequest(),
            new WeaponDisassembleRequest(), new WeaponEquipRequest(),
        ];
        foreach (CommandRequest request in requests)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", request.GetType()));
            request.RequestId = Guid.NewGuid();
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, request.GetType()));
            Assert.AreEqual(request.RequestId, json.RootElement.GetProperty("request_id").GetGuid());
            Assert.IsFalse(json.RootElement.TryGetProperty("expected_revision", out _));
            Assert.IsFalse(json.RootElement.TryGetProperty("foreground", out _));
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WeaponAcquireRequest>(
            """{"request_id":"00000000-0000-0000-0000-000000000001"}"""));
        // Unrelated skill consumers retain their existing checkpoint contract.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SkillLoadoutRequest>(
            """{"request_id":"00000000-0000-0000-0000-000000000001","skill_trees":[]}"""));
    }

    [TestMethod]
    public async Task IntentLedger_ReplaysExactlyOnceWithoutCheckpoint_RejectsChangedIntent()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        // A high current revision must not require a client whole-domain checkpoint.
        repository.FirstRows[typeof(PlayerParams)] = new PlayerParams { Value = "99" };
        var revisions = new RevisionService(argument);
        var request = new WeaponRefineRequest
        { RequestId = Guid.NewGuid(), WeaponEntryId = "owned", CrystalId = "awakening_crystal" };
        int executed = 0;
        Task<int> Action() { executed++; return Task.FromResult(7); }
        var first = await revisions.ExecuteIntent(request, "weapon.refine", nameof(WeaponInstanceModel), request, Action);
        Assert.AreEqual(99L, first.Revision);
        Assert.AreEqual(7, first.Snapshot);
        var ledger = repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single();
        repository.FirstRows[typeof(EconomyRequest)] = ledger;
        var replay = await revisions.ExecuteIntent(request, "weapon.refine", nameof(WeaponInstanceModel), request, Action);
        Assert.AreEqual(first.RequestId, replay.RequestId);
        Assert.AreEqual(first.Snapshot, replay.Snapshot);
        Assert.AreEqual(1, executed);
        request.CrystalId = "chaos_crystal";
        await Assert.ThrowsAsync<BusinessArgumentException>(() =>
            revisions.ExecuteIntent(request, "weapon.refine", nameof(WeaponInstanceModel), request, Action));
        request.CrystalId = "awakening_crystal";
        await Assert.ThrowsAsync<BusinessArgumentException>(() =>
            revisions.ExecuteIntent(request, "weapon.acquire", nameof(WeaponInstanceModel), request, Action));
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public async Task EmptyIdentity_IsRejectedBeforeAnyPersistence()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        await Assert.ThrowsAsync<BusinessArgumentException>(() =>
            new RevisionService(argument).ExecuteIntent(new CommandRequest(), "weapon.acquire",
                nameof(WeaponInstanceModel), new { }, () => Task.FromResult(1)));
        Assert.HasCount(0, repository.Calls);
    }

    [TestMethod]
    public async Task Acquire_RejectsExistingOrTombstonedKeyWithoutWriting()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        repository.FirstRows[typeof(int)] = 1;
        var commands = new WeaponCommandService(argument, new(argument), new(argument), new(argument), new(argument));
        await Assert.ThrowsAsync<BusinessArgumentException>(() => commands.Acquire(new WeaponAcquireRequest
        {
            RequestId = Guid.NewGuid(),
            Weapon = new() { Id = "shop_or_deleted", WeaponId = "sword", Rarity = "common" },
        }));
        Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        Assert.IsTrue(repository.Calls.Any(call => call.Sql?.Contains("COUNT(1) FROM Weapon") == true
            && !call.Sql.Contains("Status")));
    }

    [TestMethod]
    public async Task Refine_UsesServerRollAndOneDebit_ReplayDoesNotDebitOrRollAgain()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var weapons = new StoredWeaponService(argument);
        weapons.Stored.Add(new() { Id = "owned", WeaponId = "sword", Rarity = "common" });
        var items = new CountingItems(argument);
        var commands = new WeaponCommandService(argument, new(argument), weapons, items, new(argument));
        var request = new WeaponRefineRequest
        { RequestId = Guid.NewGuid(), WeaponEntryId = "owned", CrystalId = "awakening_crystal" };
        var first = await commands.Refine(request);
        Assert.AreEqual("fine", first.Snapshot.Weapon.Rarity);
        Assert.AreEqual(1, first.Snapshot.MaterialCount);
        Assert.AreEqual(1, weapons.Saves);
        Assert.AreEqual(1, items.Debits);
        repository.FirstRows[typeof(EconomyRequest)] =
            repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single();
        var replay = await commands.Refine(request);
        Assert.AreEqual(first.Snapshot.Weapon.Rarity, replay.Snapshot.Weapon.Rarity);
        Assert.AreEqual(1, items.Debits);
        Assert.AreEqual(1, weapons.Saves);
    }

    [TestMethod]
    public async Task Acquire_DoesNotMutateIntent_AndReplayReturnsOriginalWeapon()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var weapons = new StoredWeaponService(argument);
        var commands = new WeaponCommandService(argument, new(argument), weapons, new(argument), new(argument));
        var request = new WeaponAcquireRequest
        {
            RequestId = Guid.NewGuid(), Weapon = new() { Id = "drop", WeaponId = "sword", Rarity = "common" },
        };
        string before = JsonSerializer.Serialize(request);
        var first = await commands.Acquire(request);
        Assert.AreEqual(before, JsonSerializer.Serialize(request));
        repository.FirstRows[typeof(EconomyRequest)] =
            repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single();
        var replay = await commands.Acquire(request);
        Assert.AreEqual(first.Snapshot.Id, replay.Snapshot.Id);
        Assert.AreEqual(1, weapons.Saves);
    }

    [TestMethod]
    public async Task Disassemble_DeduplicatesAndSkipsProtectedMissing_EquivalentBatchReplays()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var weapons = new StoredWeaponService(argument);
        weapons.Stored.Add(new() { Id = "equipped", WeaponId = "sword", Rarity = "common" });
        var characters = new FixedCharacters(argument) { Model = new() { WeaponEntryId = "equipped" } };
        var commands = new WeaponCommandService(argument, new(argument), weapons, new(argument), characters);
        var request = new WeaponDisassembleRequest
        { RequestId = Guid.NewGuid(), WeaponEntryIds = ["missing", "equipped", "missing"] };
        var first = await commands.Disassemble(request);
        Assert.HasCount(0, first.Snapshot.RemovedIds);
        CollectionAssert.AreEquivalent(new[] { "missing", "equipped" }, first.Snapshot.SkippedIds);
        Assert.IsFalse(repository.Calls.Any(c => c.Sql?.StartsWith("UPDATE Weapon") == true));
        repository.FirstRows[typeof(EconomyRequest)] =
            repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single();
        request.WeaponEntryIds = ["equipped", "missing"];
        Assert.HasCount(2, (await commands.Disassemble(request)).Snapshot.SkippedIds);
        request.WeaponEntryIds.Add("different");
        await Assert.ThrowsAsync<BusinessArgumentException>(() => commands.Disassemble(request));
    }

    [TestMethod]
    public async Task Equipment_UpdatesOnlyWeaponFields_NeverSavesSkillTrees()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var character = new FixedCharacters(argument)
        {
            Model = new() { Id = Guid.NewGuid(), Kind = 3, SkillTrees = [new() { Kind = 1 }] },
        };
        repository.QueryRows[typeof(Guid)] = new[] { Guid.NewGuid() };
        var receipt = await character.EquipWeapon(new()
        {
            RequestId = Guid.NewGuid(), CharacterId = character.Model.Id!.Value, WeaponEntryId = "owned",
        });
        Assert.HasCount(1, receipt.Snapshot.SkillTrees);
        var writes = repository.Calls.Where(c => c.Name == "ExecuteAsync").ToList();
        Assert.HasCount(1, writes);
        StringAssert.Contains(writes[0].Sql!, "UPDATE Character SET WeaponEntryKey=");
        Assert.IsFalse(repository.Calls.Any(c => c.Sql?.Contains("CharacterSkill") == true));
    }

    [TestMethod]
    public async Task Materials_LockPlayerBeforeBalanceRead_RejectNegativeAndOverflowWithoutWrites()
    {
        foreach (var (balance, delta) in new[] { (0, -1), (int.MaxValue, 1) })
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            repository.FirstRows[typeof(Item)] = new Item { ItemKey = "crystal", Amount = balance };
            var service = new ItemService(argument);
            if (delta < 0)
            { await Assert.ThrowsAsync<BusinessOperationException>(() => service.AdjustAmount("crystal", delta)); }
            else
            { await Assert.ThrowsAsync<OverflowException>(() => service.AdjustAmount("crystal", delta)); }
            StringAssert.Contains(repository.Calls[0].Sql!, "FROM Player WITH (UPDLOCK, HOLDLOCK)");
            Assert.IsFalse(repository.Calls.Any(c => c.Name is "InsertAsync" or "ExecuteAsync" or "UpdateAsync"));
        }
    }

    [TestMethod]
    public async Task IntentLedger_JsonObjectKeyOrderIsNotDifferentIntent()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var revisions = new RevisionService(argument);
        var request = new CommandRequest { RequestId = Guid.NewGuid() };
        await revisions.ExecuteIntent(request, "test", nameof(WeaponInstanceModel),
            new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }, () => Task.FromResult(4));
        repository.FirstRows[typeof(EconomyRequest)] =
            repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single();
        var replay = await revisions.ExecuteIntent<int>(request, "test", nameof(WeaponInstanceModel),
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
            () => throw new AssertFailedException("Replay must not execute the action."));
        Assert.AreEqual(4, replay.Snapshot);
    }

    private sealed class StoredWeaponService(BaseServiceArgument argument) : WeaponService(argument)
    {
        public List<WeaponInstanceModel> Stored { get; } = [];
        public int Saves { get; private set; }
        public override Task<IEnumerable<WeaponInstanceModel>> GetInstanceList() =>
            Task.FromResult<IEnumerable<WeaponInstanceModel>>(Stored.ToList());
        protected override Task SaveSnapshot(WeaponInstanceModel model)
        {
            Saves++;
            Stored.RemoveAll(w => w.Id == model.Id);
            Stored.Add(model);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingItems(BaseServiceArgument argument) : ItemService(argument)
    {
        public int Debits { get; private set; }
        public override Task<int> AdjustAmount(string key, int delta, string? category = null)
        {
            Assert.AreEqual("awakening_crystal", key);
            Assert.AreEqual(-1, delta);
            Debits++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedCharacters(BaseServiceArgument argument) : CharacterService(argument)
    {
        public CharacterModel Model { get; set; } = new();
        public override Task<CharacterModel?> Get(Guid id) => Task.FromResult<CharacterModel?>(Model);
        public override Task<List<CharacterModel>> GetList() => Task.FromResult(new List<CharacterModel> { Model });
    }
}
