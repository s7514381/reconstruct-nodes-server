using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    private static readonly string[] EconomyRoutes = ["GameProgress/BackgroundSync (wave_starts)", "GameProgress/BackgroundSync (kills)"];
    private static readonly string[] SaveRoutes = ["GameProgress/BackgroundSync (progress)", "GameProgress/BackgroundSync (achievement)", "GameProgress/BackgroundSync (talent)"];
    private static readonly string[] ShopRoutes = ["Shop/Draw (count=1)", "Shop/Draw (count=10)"];

    private static bool SelectionComplete(IEnumerable<string> actual, IEnumerable<string> expected,
        bool correlated, bool cleaned, bool checksPassed) =>
        correlated && cleaned && checksPassed && actual.Order().SequenceEqual(expected.Order());

    private static async Task AssertReceiptCount(DapperRepository repository, Guid player, Guid request, int count)
    {
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND RequestId=@Request",
            new { Player = player, Request = request }) == count, "exact fixture command receipt count");
    }

    private static async Task<long> Gold(HttpClient http) =>
        (await Post(http, "Shop/GetGold", new { })).Data!.GetValue<long>();

    public static void SelectedModesSelfTest()
    {
        foreach (var routes in new[] { EconomyRoutes, SaveRoutes, ShopRoutes })
        {
            Require(SelectionComplete(routes, routes, true, true, true), "selected workloads complete");
            Require(!SelectionComplete(routes.Take(routes.Length - 1), routes, true, true, true) &&
                !SelectionComplete(routes.Append(routes[0]), routes, true, true, true) &&
                !SelectionComplete(routes.Skip(1).Append("wrong/route"), routes, true, true, true) &&
                !SelectionComplete(routes, routes, false, true, true) &&
                !SelectionComplete(routes, routes, true, false, true) &&
                !SelectionComplete(routes, routes, true, true, false), "selected partial workloads incomplete");
            Require(!LatencyComplete(routes.Length, true, true, "completed"), "selected run is not full baseline");
        }
        Console.WriteLine("Economy/save/shop selection and completion self-tests passed; no database or HTTP access.");
    }
}
