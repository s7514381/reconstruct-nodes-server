using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// Player-row lock is the common serialization boundary for receipts and snapshots.
/// Uses the existing parameter table; no destructive schema migration is required.
/// </summary>
public class RevisionService(BaseServiceArgument argument) : _ModelService<PlayerParams, PlayerParams>(argument)
{
    public const string RevisionGroup = "server.revisions";

    public async Task<RevisionVector> GetVector()
    {
        await LockPlayerAsync();
        return new RevisionVector
        {
            Talent = await Read(nameof(TalentModel)), Character = await Read(nameof(CharacterModel)),
            GameProgress = await Read(nameof(GameProgressModel)), Achievement = await Read(nameof(AchievementProgressModel)),
            Weapon = await Read(nameof(WeaponInstanceModel)),
        };
    }

    public async Task<long> Read(string domain)
    {
        var row = await ReadRow(domain);
        return row == null ? 0 : long.Parse(row.Value, CultureInfo.InvariantCulture);
    }

    private Task<PlayerParams?> ReadRow(string domain) => QueryFirstAsync<PlayerParams?>(
        "SELECT * FROM PlayerParams WHERE CreateUserId=@CreateUserId AND [Group]=@Group AND Name=@Name AND Status != -1",
        new { CreateUserId = _playerId, Group = RevisionGroup, Name = domain });

    public static IReadOnlyCollection<string>? ScopesFor(IRevisionedSnapshot snapshot) => snapshot switch
    {
        CharacterModel { Id: { } id } => [id.ToString("D")],
        WeaponInstanceModel weapon => [weapon.Id],
        _ => null,
    };

    private static string ScopeKey(string domain, string scope) =>
        domain + "/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope)));

    private async Task<string[]> ResolveScopes(string domain, IReadOnlyCollection<string> scopes)
    {
        if (domain != nameof(WeaponInstanceModel)) { return scopes.Distinct().ToArray(); }
        // Natural-key equality follows SQL collation. Resolve stored spelling as well,
        // otherwise a case/accent alias could bypass a fenced weapon's revision.
        var stored = await QueryAsync<string>(
            "SELECT EntryKey FROM Weapon WHERE CreateUserId=@CreateUserId AND EntryKey IN @Keys",
            new { CreateUserId = _playerId, Keys = scopes.Distinct().ToArray() });
        return scopes.Concat(stored).Distinct().ToArray();
    }

    public async Task<long> Check(string domain, long? expected, IReadOnlyCollection<string>? scopes = null)
    {
        await LockPlayerAsync();
        long current = await Read(domain);
        long fence = current;
        if (current > 0 && scopes is { Count: > 0 })
        {
            string[] resolved = await ResolveScopes(domain, scopes);
            var rows = await QueryAsync<PlayerParams>(
                "SELECT * FROM PlayerParams WHERE CreateUserId=@CreateUserId AND [Group]=@Group AND Name IN @Names AND Status != -1",
                new { CreateUserId = _playerId, Group = RevisionGroup, Names = resolved.Select(scope => ScopeKey(domain, scope)).ToArray() });
            fence = rows.Select(row => long.Parse(row.Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        }
        // Unrelated character/weapon instances can merge; shared state uses a domain fence.
        // Unversioned in-flight snapshots cannot erase an already fenced foreground result.
        if (expected < 0 || (expected.HasValue ? expected > current || expected < fence : fence != 0))
        {
            throw new BusinessOperationException(new ApiMessage("concurrency.revision_conflict",
                new Dictionary<string, string> { ["domain"] = domain, ["revision"] = current.ToString(CultureInfo.InvariantCulture) }));
        }
        return current;
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

    public async Task<CommandReceipt<T>> Execute<T>(ForegroundRequest request, string operation, string domain,
        object payload, Func<Task<T>> action, IReadOnlyCollection<string>? scopes = null)
        => await ExecuteCore(request, operation, domain, payload, action,
            () => Check(domain, request.ExpectedRevision, scopes));

    /// <summary>Intent commands serialize with the same player lock and receipt ledger,
    /// but never depend on a client domain checkpoint.</summary>
    public Task<CommandReceipt<T>> ExecuteIntent<T>(CommandRequest request, string operation, string domain,
        object payload, Func<Task<T>> action) =>
        ExecuteCore(request, operation, domain, payload, action, null, canonicalPayload: true);

    private async Task<CommandReceipt<T>> ExecuteCore<T>(CommandRequest request, string operation, string domain,
        object payload, Func<Task<T>> action, Func<Task<long>>? check, bool canonicalPayload = false)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        await LockPlayerAsync();
        string json = canonicalPayload ? CanonicalPayload(payload) : JsonSerializer.Serialize(payload);
        EconomyRequest? prior = await QueryFirstAsync<EconomyRequest?>(
            "SELECT * FROM EconomyRequest WHERE CreateUserId=@CreateUserId AND RequestId=@RequestId",
            new { CreateUserId = _playerId, request.RequestId });
        // Replay precedes optional revision checks; preserve the original input intent.
        if (prior != null)
        {
            if (prior.Operation != operation || prior.Payload != json)
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict);
            }
            return JsonSerializer.Deserialize<CommandReceipt<T>>(prior.Result)
                ?? throw new InvalidOperationException("Invalid command receipt.");
        }
        if (check != null) { await check(); }
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
    private static string CanonicalPayload(object payload)
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
