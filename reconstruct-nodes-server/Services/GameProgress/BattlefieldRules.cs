using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>Formal editor validation, matching BattlefieldLayout's deterministic entrance traversal.</summary>
public static class BattlefieldRules
{
    private readonly record struct Cell(int X, int Y)
    {
        public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);
        public static Cell operator -(Cell a) => new(-a.X, -a.Y);
    }
    private static readonly Cell[] Directions = [new(1,0), new(0,1), new(-1,0), new(0,-1)];

    /// <summary>隊長不可空缺：有部署角色就必須指定其中一位，沒有部署角色就不能有隊長。</summary>
    public static void ValidateCaptain(Guid? captain, List<GameProgressDeploymentModel> deployments)
    {
        bool valid = deployments.Count == 0
            ? captain == null
            : captain is { } id && deployments.Any(row => row.CharacterId == id);
        if (!valid) { throw new BusinessArgumentException("battlefield.captain_invalid"); }
    }

    public static void Validate(GameProgressBattlefieldModel? battlefield, List<GameProgressDeploymentModel> deployments)
    {
        if (deployments.Select(d => d.CharacterId).Distinct().Count() != deployments.Count)
        { throw new BusinessArgumentException("battlefield.character_duplicate"); }
        if (battlefield == null) { return; } // legacy non-grid deployment is still supported.
        if (battlefield.Version != 1 || battlefield.Columns != 7 || battlefield.Rows != 10 ||
            battlefield.Castle is not { Count: 2 } || battlefield.Roads == null)
        { throw new BusinessArgumentException("battlefield.invalid_version"); }
        Cell castle = new(battlefield.Castle[0], battlefield.Castle[1]);
        bool Contains(Cell cell) => cell.X is >= 0 and < 7 && cell.Y is >= 0 and < 10;
        bool IsCastle(Cell cell) => cell.X >= castle.X && cell.X < castle.X + 2 && cell.Y >= castle.Y && cell.Y < castle.Y + 2;
        if (!Contains(castle) || !Contains(castle + new Cell(1,1))) { throw new BusinessArgumentException("battlefield.castle_inside"); }
        Dictionary<Cell, GameProgressRoadModel> roads = [];
        foreach (var road in battlefield.Roads)
        {
            if (road == null || road.Rotation is < 0 or > 3) { throw new BusinessArgumentException("battlefield.invalid_direction"); }
            Cell cell = new(road.X, road.Y);
            if (!Contains(cell) || IsCastle(cell)) { throw new BusinessArgumentException("battlefield.road_outside"); }
            if (!roads.TryAdd(cell, road)) { throw new BusinessArgumentException("battlefield.duplicate_tile"); }
        }
        Cell[] Openings(Cell cell)
        {
            var road = roads[cell];
            return [Directions[road.Rotation], Directions[(road.Rotation + (road.Corner ? 1 : 2)) % 4]];
        }
        List<Cell> buildSpots = [];
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 7; x++)
        {
            Cell cell = new(x,y);
            if (!IsCastle(cell) && !roads.ContainsKey(cell)) { buildSpots.Add(cell); }
        }
        HashSet<Cell> occupied = [];
        foreach (var deployment in deployments)
        {
            Cell cell;
            if (deployment.Cell is { Count: 2 }) { cell = new(deployment.Cell[0], deployment.Cell[1]); }
            else if (deployment.Cell == null && deployment.SpotIndex > 0 && deployment.SpotIndex <= buildSpots.Count)
            { cell = buildSpots[deployment.SpotIndex - 1]; }
            else { throw new BusinessArgumentException("battlefield.occupied"); }
            if (!Contains(cell) || IsCastle(cell) || roads.ContainsKey(cell) || !occupied.Add(cell))
            { throw new BusinessArgumentException("battlefield.occupied"); }
        }
        HashSet<Cell> visited = [];
        int paths = 0;
        foreach (Cell start in roads.Keys.OrderBy(c => c.Y).ThenBy(c => c.X))
        foreach (Cell outward in Openings(start))
        {
            if (Contains(start + outward)) { continue; }
            Cell current = start, incoming = outward;
            while (true)
            {
                if (!visited.Add(current)) { throw new BusinessArgumentException("battlefield.loop"); }
                Cell[] ports = Openings(current);
                Cell outgoing = ports[0] == incoming ? ports[1] : ports[0];
                Cell next = current + outgoing;
                if (IsCastle(next)) { paths++; break; }
                if (!roads.ContainsKey(next) || !Openings(next).Contains(-outgoing))
                { throw new BusinessArgumentException("battlefield.broken"); }
                current = next;
                incoming = -outgoing;
            }
        }
        if (paths == 0) { throw new BusinessArgumentException("battlefield.need_entrance"); }
        if (visited.Count != roads.Count) { throw new BusinessArgumentException("battlefield.disconnected"); }
    }
}
