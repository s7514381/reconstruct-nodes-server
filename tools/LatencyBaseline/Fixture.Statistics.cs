internal static partial class Fixture
{
    private static bool LatencyComplete(int workloads, bool correlated, bool cleaned, string stage) =>
        workloads == 18 && correlated && cleaned && stage == "completed";
    private sealed record LatencyStats(double MeanMs, double P50Ms, double P95Ms, double MaxMs)
    {
        public bool P95Below100Ms => P95Ms < 100;
    }

    // Nearest rank, matching the Python report; first requests are never inputs.
    private static LatencyStats Statistics(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        Require(values.Length > 0 && values.All(v => double.IsFinite(v) && v >= 0), "valid timing samples");
        double Rank(double p) => values[(int)Math.Ceiling(values.Length * p) - 1];
        return new(values.Average(), Rank(.50), Rank(.95), values[^1]);
    }

    private static void ValidateCorrelation(List<RouteReport> routes, List<ConcurrentPair> pairs)
    {
        Require(DuplicateTimingIds == 0, "unique middleware timing events");
        foreach (var pair in ItemCorrelations)
            CorrelatedElapsed(pair.Key, pair.Value);
        foreach (var pair in RegressionCorrelations)
            CorrelatedElapsed(pair.Key, pair.Value);
        var samples = routes.SelectMany(r => r.Samples.Prepend(r.First))
            .Concat(pairs.SelectMany(p => new[] { p.Save, p.Kill })).ToArray();
        var ids = samples.Select(s => s.RequestId).Concat(ItemCorrelations.Keys).Concat(RegressionCorrelations.Keys).ToArray();
        Require(ids.Distinct().Count() == ids.Length, "unique timing correlation");
        double Elapsed(Sample sample) => CorrelatedElapsed(sample.RequestId, 200);
        foreach (var sample in samples) Elapsed(sample);
        foreach (var route in routes) route.Backend = Statistics(route.Samples.Select(Elapsed));
    }

    private static double CorrelatedElapsed(string id, int status)
    {
        Require(Breakdown.TryGetValue(id, out var record), "complete timing correlation");
        Require(record!["RequestId"]!.GetValue<string>() == id &&
            record["StatusCode"]!.GetValue<int>() == status, "timing status correlation");
        double elapsed = record["ElapsedMs"]!.GetValue<double>();
        Require(double.IsFinite(elapsed) && elapsed >= 0, "valid middleware elapsed");
        return elapsed;
    }

    public static void StatisticsSelfTest()
    {
        Require(!LatencyComplete(17, true, true, "completed") &&
            !LatencyComplete(0, true, true, "item regressions completed") &&
            !LatencyComplete(18, false, true, "completed") &&
            !LatencyComplete(18, true, false, "completed") &&
            !LatencyComplete(18, true, true, "final readback") &&
            LatencyComplete(18, true, true, "completed"), "full workload completion semantics");
        var stats = Statistics(Enumerable.Range(1, 100).Select(i => (double)i));
        Require(stats.MeanMs == 50.5 && stats.P50Ms == 50 && stats.P95Ms == 95 &&
            stats.MaxMs == 100 && stats.P95Below100Ms, "nearest rank statistics");
        Require(!Statistics([100]).P95Below100Ms, "strict threshold");
        var route = new RouteReport("test", new(999, "first"), [new(110, "measured")], 110, false);
        Breakdown["first"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{"RequestId":"first","StatusCode":200,"ElapsedMs":999}""")!;
        Breakdown["measured"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{"RequestId":"measured","StatusCode":200,"ElapsedMs":99}""")!;
        try
        {
            ValidateCorrelation([route], []);
            Require(route.Backend!.P95Ms == 99 && route.Http.P95Ms == 110,
                "first excluded and HTTP independent");
            bool rejected = false;
            try { ValidateCorrelation([route, route], []); }
            catch (FixtureAssertion) { rejected = true; }
            Require(rejected, "duplicate correlation rejected");
            Breakdown["rejected"] = System.Text.Json.Nodes.JsonNode.Parse(
                """{"RequestId":"rejected","StatusCode":400,"ElapsedMs":1}""")!;
            ItemCorrelations["rejected"] = 400;
            Breakdown["conflict"] = System.Text.Json.Nodes.JsonNode.Parse(
                """{"RequestId":"conflict","StatusCode":409,"ElapsedMs":1}""")!;
            RegressionCorrelations["conflict"] = 409;
            ValidateCorrelation([], []);
        }
        finally { Breakdown.Clear(); ItemCorrelations.Clear(); RegressionCorrelations.Clear(); }
        try { ValidateCorrelation([new("test", new(1, "missing"), [new(1, "also-missing")], 1, true)], []); }
        catch (FixtureAssertion) { Console.WriteLine("Statistics self-tests passed."); return; }
        throw new InvalidOperationException("Missing correlation accepted");
    }
}
