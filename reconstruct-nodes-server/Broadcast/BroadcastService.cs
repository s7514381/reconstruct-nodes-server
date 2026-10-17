namespace ReconstructNodesServer.Broadcast;

public enum BroadcastOutcome
{
    Ok,
    Invalid,
    LimitReached,
    NotFound,
}

public readonly record struct BroadcastResult<T>(BroadcastOutcome Outcome, T? Data, string? InvalidField = null);

/// <summary>廣播業務規則：驗證、容量上限與過期清理；儲存細節留給 <see cref="IBroadcastStore"/>。</summary>
public sealed class BroadcastService(IBroadcastStore store, TimeProvider time)
{
    public async Task<BroadcastResult<IReadOnlyList<BroadcastItem>>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BroadcastItem> items = await store.ListAsync(cancellationToken);
        return new(BroadcastOutcome.Ok, items);
    }

    public async Task<BroadcastResult<BroadcastItem>> PublishAsync(BroadcastPublishRequest request, CancellationToken cancellationToken)
    {
        long now = time.GetUtcNow().ToUnixTimeSeconds();
        string? invalid = BroadcastValidator.TryBuild(request, now, out BroadcastItem item);
        if (invalid != null) { return new(BroadcastOutcome.Invalid, null, invalid); }

        IReadOnlyList<BroadcastItem> existing = await store.ListAsync(cancellationToken);
        var active = new List<BroadcastItem>();
        foreach (BroadcastItem other in existing)
        {
            if (BroadcastValidator.IsExpired(other, now))
            {
                await store.RemoveAsync(other.Id, cancellationToken);
            }
            else if (other.Id != item.Id)
            {
                active.Add(other);
            }
        }

        if (active.Count >= BroadcastValidator.MaxActiveItems) { return new(BroadcastOutcome.LimitReached, null); }

        await store.PutAsync(item, cancellationToken);
        return new(BroadcastOutcome.Ok, item);
    }

    public async Task<BroadcastResult<string>> RemoveAsync(string? id, CancellationToken cancellationToken)
    {
        if (!BroadcastValidator.IsValidId(id)) { return new(BroadcastOutcome.Invalid, null, "id"); }
        bool removed = await store.RemoveAsync(id!, cancellationToken);
        return new(removed ? BroadcastOutcome.Ok : BroadcastOutcome.NotFound, id);
    }
}
