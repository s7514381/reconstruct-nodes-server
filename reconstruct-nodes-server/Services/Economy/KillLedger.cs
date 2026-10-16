namespace ReconstructNodesServer.Services;

/// <summary>
/// 已領取擊殺的位元圖，存在單一 PlayerParams 列（economy / claimed_kills），
/// 格式為 "{sessionId:D}:{base64 位元組};..."，取代「每殺一列」的 EconomyRequest 收據。
/// 只保留最近 <see cref="MaxSessions"/> 個波次（最新在後），開新波次時加入並淘汰最舊的，
/// 因此資料量與擊殺總數無關。
/// </summary>
public sealed class KillLedger
{
    public const string Group = "economy";
    public const string Name = "claimed_kills";
    public const int MaxSessions = 4;

    private readonly List<(Guid Session, byte[] Bits)> _sessions = [];

    public static KillLedger Parse(string? value)
    {
        var ledger = new KillLedger();
        if (string.IsNullOrEmpty(value)) { return ledger; }
        foreach (string entry in value.Split(';'))
        {
            if (entry.Length < 37 || entry[36] != ':' || !Guid.TryParse(entry.AsSpan(0, 36), out var session))
            { throw new InvalidOperationException("Invalid claimed kill ledger."); }
            ledger._sessions.Add((session, Convert.FromBase64String(entry[37..])));
        }
        return ledger;
    }

    public bool Contains(Guid session) => _sessions.Exists(s => s.Session == session);

    public bool Has(Guid session, int index)
    {
        int at = _sessions.FindIndex(s => s.Session == session);
        return at >= 0 && (index >> 3) < _sessions[at].Bits.Length &&
            (_sessions[at].Bits[index >> 3] & (1 << (index & 7))) != 0;
    }

    public void Add(Guid session, int index)
    {
        int at = _sessions.FindIndex(s => s.Session == session);
        if (at < 0) { Start(session); at = _sessions.Count - 1; }
        byte[] bits = _sessions[at].Bits;
        if ((index >> 3) >= bits.Length) { Array.Resize(ref bits, (index >> 3) + 1); }
        bits[index >> 3] |= (byte)(1 << (index & 7));
        _sessions[at] = (session, bits);
    }

    /// <summary>開始（或重新開始）一個波次的位元圖，並只保留最近 <see cref="MaxSessions"/> 個。</summary>
    public void Start(Guid session)
    {
        _sessions.RemoveAll(s => s.Session == session);
        _sessions.Add((session, []));
        if (_sessions.Count > MaxSessions) { _sessions.RemoveRange(0, _sessions.Count - MaxSessions); }
    }

    public IEnumerable<int> Indexes(Guid session)
    {
        int at = _sessions.FindIndex(s => s.Session == session);
        if (at < 0) { yield break; }
        byte[] bits = _sessions[at].Bits;
        for (int i = 0; i < bits.Length * 8; i++)
        {
            if ((bits[i >> 3] & (1 << (i & 7))) != 0) { yield return i; }
        }
    }

    public override string ToString() =>
        string.Join(';', _sessions.Select(s => $"{s.Session:D}:{Convert.ToBase64String(s.Bits)}"));
}
