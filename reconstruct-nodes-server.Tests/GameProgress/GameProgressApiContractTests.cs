using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Controllers;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Tests.GameProgress
{
    /// <summary>
    /// 驗證 action 改名後的 API 簽章與既有 JSON 契約。
    /// 與 Program.cs 相同：JsonOptions 的 PropertyNamingPolicy = null。
    /// </summary>
    [TestClass]
    public class GameProgressApiContractTests
    {
        private static readonly JsonSerializerOptions ApiJsonOptions = new() { PropertyNamingPolicy = null };

        private static readonly string[] ExpectedTopLevelKeys =
        {
            "version", "deployment_log", "wave", "gold", "max_wave_reached",
            "unlocked_bullet_kinds", "unlocked_auxiliary_kinds", "unlocked_character_kinds",
            "skill_suffixes", "refine_item_counts", "progression", "character_weapons",
            "difficulty", "lifetime_enemies_defeated", "infinite_level", "cleared_waves",
            "wave_completed",
        };

        [TestMethod]
        public void Controller_RouteTemplate_IsUnchanged()
        {
            var routes = typeof(GameProgressController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .ToArray();

            Assert.HasCount(1, routes);
            Assert.AreEqual("[controller]/[action]", routes[0].Template);
        }

        [TestMethod]
        public void Controller_ActionNames_AreRenamedToGetAndSave()
        {
            var actions = typeof(GameProgressController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "Get", "Save" }, actions);
        }

        [TestMethod]
        public void Save_BindsGameProgressModel()
        {
            var method = typeof(GameProgressController).GetMethod("Save");
            Assert.IsNotNull(method);
            var parameters = method.GetParameters();
            Assert.HasCount(1, parameters);
            Assert.AreEqual(typeof(GameProgressModel), parameters[0].ParameterType);
        }

        [TestMethod]
        public void GameProgressModel_TopLevelJsonKeys_AreUnchanged()
        {
            // Battlefield 為 null 時不輸出，其餘鍵必須與前端 save_service 的 payload 一致
            var json = JsonSerializer.Serialize(new GameProgressModel(), ApiJsonOptions);
            using var document = JsonDocument.Parse(json);
            var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

            CollectionAssert.AreEquivalent(ExpectedTopLevelKeys, keys);
        }

        [TestMethod]
        public void GameProgressModel_DeserializesFrontendPayload()
        {
            const string payload = """
                {
                  "version": 3,
                  "deployment_log": [
                    { "spot_index": 2, "character_name": "tester", "wave": 5, "kind": 1, "skill_loadout": [], "cell": [1, 2] }
                  ],
                  "wave": 5,
                  "gold": 120,
                  "max_wave_reached": 6,
                  "unlocked_bullet_kinds": {},
                  "unlocked_auxiliary_kinds": {},
                  "unlocked_character_kinds": {},
                  "skill_suffixes": {},
                  "refine_item_counts": {},
                  "progression": {},
                  "character_weapons": {},
                  "difficulty": 1,
                  "lifetime_enemies_defeated": 42,
                  "infinite_level": 0,
                  "cleared_waves": [],
                  "wave_completed": true
                }
                """;

            var model = JsonSerializer.Deserialize<GameProgressModel>(payload, ApiJsonOptions);

            Assert.IsNotNull(model);
            Assert.AreEqual(3, model.Version);
            Assert.AreEqual(5, model.Wave);
            Assert.AreEqual(120L, model.Gold);
            Assert.AreEqual(42L, model.LifetimeEnemiesDefeated);
            Assert.IsTrue(model.WaveCompleted);
            Assert.IsNull(model.Battlefield);
            Assert.HasCount(1, model.DeploymentLog);
            Assert.AreEqual(2, model.DeploymentLog[0].SpotIndex);
            Assert.AreEqual("tester", model.DeploymentLog[0].CharacterName);
            CollectionAssert.AreEqual(new List<int> { 1, 2 }, model.DeploymentLog[0].Cell);
        }

        [TestMethod]
        public void GameProgressDeploymentModel_OptionalKeys_OmittedWhenNull()
        {
            var json = JsonSerializer.Serialize(new GameProgressDeploymentModel(), ApiJsonOptions);
            using var document = JsonDocument.Parse(json);
            var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

            Assert.IsTrue(keys.IsSupersetOf(new[] { "spot_index", "character_name", "wave", "kind", "skill_loadout" }));
            Assert.DoesNotContain("cell", keys);
            Assert.DoesNotContain("weapon_quality_version", keys);
        }
    }
}
