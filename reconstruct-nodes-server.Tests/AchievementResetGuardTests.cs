using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

[TestClass]
public class AchievementResetGuardTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [TestMethod]
    public void Development_is_allowed() =>
        AchievementResetCommand.EnsureAllowed(new FakeEnvironment(Environments.Development));

    [TestMethod]
    [DataRow("Production")]
    [DataRow("Staging")]
    public void Other_environments_are_rejected(string name)
    {
        var error = Assert.ThrowsExactly<BusinessOperationException>(
            () => AchievementResetCommand.EnsureAllowed(new FakeEnvironment(name)));
        Assert.AreEqual(ApiMessageKeys.AchievementResetUnavailable, error.ApiMessage.Key);
    }
}
