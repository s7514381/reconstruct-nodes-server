using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// Snapshot transactions detect resource write conflicts; unique receipt and
/// parameter keys protect first writes without serializing the player's other resources.
/// </summary>
public class RevisionCommand(BaseServiceArgument argument) : BaseService(argument)
{
    public const string RevisionGroup = "server.revisions";
    public async Task<RevisionVector> GetVector()
    {
        return new RevisionVector
        {
            Talent = await Read(nameof(TalentModel)), Character = await Read(nameof(CharacterModel)),
            GameProgress = await Read(nameof(GameProgressModel)), Achievement = await Read(nameof(AchievementProgressModel)),
            Weapon = await Read(nameof(WeaponInstanceModel)),
        };
    }

    /// <summary>讀取單一領域 revision 值的 SQL；domain 只能是 nameof(XxxModel) 之類的固定名稱。</summary>
    public static string SelectValueSql(string domain) =>
        OwnedSql.Select<PlayerParams>($"[Group] = '{RevisionGroup}' AND Name = '{domain}'", "Value");

    public SnapshotRead<RevisionVector> VectorRead => new(
        OwnedSql.Select<PlayerParams>($"[Group] = '{RevisionGroup}'", "Name, Value"), async reader =>
    {
        var values = (await reader.ReadAsync<PlayerParams>())
            .ToDictionary(row => row.Name, row => long.Parse(row.Value, CultureInfo.InvariantCulture));
        long Of(string domain) => values.GetValueOrDefault(domain);
        return new RevisionVector
        {
            Talent = Of(nameof(TalentModel)), Character = Of(nameof(CharacterModel)),
            GameProgress = Of(nameof(GameProgressModel)), Achievement = Of(nameof(AchievementProgressModel)),
            Weapon = Of(nameof(WeaponInstanceModel)),
        };
    });

    public async Task<long> Read(string domain)
    {
        var row = await ReadRow(domain);
        return row == null ? 0 : long.Parse(row.Value, CultureInfo.InvariantCulture);
    }

    private Task<PlayerParams?> ReadRow(string domain) => QueryFirstAsync<PlayerParams?>(
        OwnedSql.Select<PlayerParams>("[Group]=@Group AND Name=@Name"),
        new { CreateUserId = _playerId, Group = RevisionGroup, Name = domain });

    public static IReadOnlyCollection<string>? ScopesFor(IRevisionedSnapshot snapshot) => snapshot switch
    {
        CharacterModel { Id: { } id } => [id.ToString("D")],
        WeaponInstanceModel weapon => [weapon.Id],
        _ => null,
    };

    internal static string ScopeKey(string domain, string scope) =>
        domain + "/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope)));

    private async Task<string[]> ResolveScopes(string domain, IReadOnlyCollection<string> scopes)
    {
        if (domain != nameof(WeaponInstanceModel)) { return scopes.Distinct().ToArray(); }
        // Natural-key equality follows SQL collation. Resolve stored spelling as well,
        // otherwise a case/accent alias could bypass a fenced weapon's revision.
        var stored = await QueryAsync<string>(
            OwnedSql.Select<Weapon>("EntryKey IN @Keys", "EntryKey", activeOnly: false),
            new { CreateUserId = _playerId, Keys = scopes.Distinct().ToArray() });
        return scopes.Concat(stored).Distinct().ToArray();
    }

    public async Task<long> Check(string domain, long? expected, IReadOnlyCollection<string>? scopes = null)
    {
        long current = await Read(domain);
        long fence = current;
        if (current > 0 && scopes is { Count: > 0 })
        {
            string[] resolved = await ResolveScopes(domain, scopes);
            var rows = await QueryAsync<PlayerParams>(
                OwnedSql.Select<PlayerParams>("[Group]=@Group AND Name IN @Names"),
                new { CreateUserId = _playerId, Group = RevisionGroup, Names = resolved.Select(scope => ScopeKey(domain, scope)).ToArray() });
            fence = rows.Select(row => long.Parse(row.Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        }
        return CheckValues(domain, expected, current, fence);
    }

    /// <summary>Check 的純判斷：current 為領域 revision，fence 為範圍 revision（無範圍時等於 current）。</summary>
    internal static long CheckValues(string domain, long? expected, long current, long fence)
    {
        // Unrelated character/weapon instances can merge; shared state uses a domain fence.
        // Unversioned in-flight snapshots cannot erase an already fenced foreground result.
        if (expected < 0 || (expected.HasValue ? expected > current || expected < fence : fence != 0))
        {
            throw new BusinessOperationException(new ApiMessage("concurrency.revision_conflict",
                new Dictionary<string, string> { ["domain"] = domain, ["revision"] = current.ToString(CultureInfo.InvariantCulture) }));
        }
        return current;
    }

    // ---- 批次命令（ExecuteAtomicJsonAsync）共用：回執讀取／重播、revision 讀取／推進、回執寫入 ----

    /// <summary>以 RequestId 讀取回執；回執沒有軟刪除語意，不過濾 Status。</summary>
    internal static SnapshotRead<List<EconomyRequest>> ReceiptsRead(Action<List<EconomyRequest>> assign,
        string condition = "RequestId = @RequestId") =>
        OwnedSql.Rows<EconomyRequest>(condition, assign, activeOnly: false);

    /// <summary>讀取領域與範圍 revision 列；參數 @RevisionNames 由 <see cref="RevisionNames"/> 提供。</summary>
    internal static SnapshotRead<List<PlayerParams>> RevisionsRead(Action<List<PlayerParams>> assign) =>
        OwnedSql.Rows<PlayerParams>($"[Group] = '{RevisionGroup}' AND Name IN @RevisionNames", assign);

    internal static string[] RevisionNames(string domain, IEnumerable<string>? scopes = null) =>
        [domain, .. (scopes ?? []).Select(scope => ScopeKey(domain, scope))];

    /// <summary>同 RequestId 已有回執：同意圖回原回執（單一 SELECT），不同意圖回 EconomyRequestConflict；沒有回執回 null。</summary>
    internal static IReadOnlyList<SqlBatchCommand>? Replay(EconomyRequest? prior, string operation, string payload)
    {
        if (prior == null) { return null; }
        if (prior.Operation != operation || prior.Payload != payload)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
        return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result })];
    }

    internal static long CurrentValue(IEnumerable<PlayerParams> rows, string name) =>
        rows.FirstOrDefault(row => row.Group == RevisionGroup && row.Name == name) is { } found
            ? long.Parse(found.Value, CultureInfo.InvariantCulture) : 0;

    /// <summary>Advance 的批次版：領域與各範圍 revision 都寫成同一個新值，回傳該值。</summary>
    internal static long AdvanceRows(SqlWriteBatch writes, IEnumerable<PlayerParams> rows, string domain,
        IEnumerable<string>? scopes = null)
    {
        PlayerParams[] existing = rows.ToArray();
        long next = checked(CurrentValue(existing, domain) + 1);
        string value = next.ToString(CultureInfo.InvariantCulture);
        List<PlayerParams> created = [], changed = [];
        foreach (string name in RevisionNames(domain, scopes).Distinct())
        {
            PlayerParams? row = existing.FirstOrDefault(r => r.Group == RevisionGroup && r.Name == name);
            if (row == null) { created.Add(new PlayerParams { Group = RevisionGroup, Name = name, Value = value }); }
            else { row.Value = value; changed.Add(row); }
        }
        writes.Insert(created);
        writes.Update(changed);
        return next;
    }

    /// <summary>寫入永久回執並以 SELECT 回傳序列化結果（批次的最後一個命令）。</summary>
    internal static void CompleteReceipt<T>(SqlWriteBatch writes, CommandRequest request, string operation,
        string payload, long revision, T snapshot)
    {
        string result = JsonSerializer.Serialize(new CommandReceipt<T>
        { RequestId = request.RequestId, Revision = revision, Snapshot = snapshot });
        writes.Insert([new EconomyRequest { RequestId = request.RequestId, Operation = operation,
            Payload = payload, Result = result }]);
        writes.Add("SELECT @Result;", new { Result = result });
    }

    public async Task<long> Advance(string domain, IReadOnlyCollection<string>? scopes = null)
    {
        PlayerParams? row = await ReadRow(domain);
        long next = checked((row == null ? 0 : long.Parse(row.Value, CultureInfo.InvariantCulture)) + 1);
        await WriteRevision(domain, next, row);
        string[] resolved = scopes is { Count: > 0 } ? await ResolveScopes(domain, scopes) : [];
        foreach (string scope in resolved)
        {
            string key = ScopeKey(domain, scope);
            await WriteRevision(key, next, await ReadRow(key));
        }
        return next;
    }

    private async Task WriteRevision(string domain, long revision, PlayerParams? row)
    {
        if (row == null)
        {
            await InsertAsync(new PlayerParams { CreateUserId = _playerId, Group = RevisionGroup, Name = domain, Value = revision.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            row.Value = revision.ToString(CultureInfo.InvariantCulture);
            await UpdateAsync(row);
        }
    }

    /// <summary>Intent commands share the durable receipt ledger and snapshot transaction,
    /// but never depend on a client domain checkpoint.</summary>
    public async Task<CommandReceipt<T>> ExecuteIntent<T>(CommandRequest request, string operation, string domain,
        object payload, Func<Task<T>> action)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        string json = CanonicalPayload(payload);
        EconomyRequest? prior = await QueryFirstAsync<EconomyRequest?>(
            OwnedSql.Select<EconomyRequest>("RequestId = @RequestId", activeOnly: false),
            new { CreateUserId = _playerId, request.RequestId });
        // Replay precedes any other work; preserve the original input intent.
        if (prior != null)
        {
            if (prior.Operation != operation || prior.Payload != json)
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict);
            }
            return JsonSerializer.Deserialize<CommandReceipt<T>>(prior.Result)
                ?? throw new InvalidOperationException("Invalid command receipt.");
        }
        T snapshot = await action();
        var receipt = new CommandReceipt<T> { RequestId = request.RequestId, Revision = await Read(domain), Snapshot = snapshot };
        await InsertAsync(new EconomyRequest
        {
            CreateUserId = _playerId, RequestId = request.RequestId, Operation = operation,
            Payload = json, Result = JsonSerializer.Serialize(receipt),
        });
        return receipt;
    }

    // JSON object property order is not business intent. Keep array order (affix order
    // can be meaningful); set-valued actions normalize their lists before calling us.
    internal static string CanonicalPayload(object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(document.RootElement, writer);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());

        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) { Write(item, writer); }
                writer.WriteEndArray();
            }
            else { value.WriteTo(writer); }
        }
    }
}
