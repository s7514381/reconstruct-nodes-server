using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Controllers;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services
{
    /// <summary>
    /// 驗證 Service 冗餘前綴改名後的新簽章，且舊名已移除；
    /// 同時確認 Controller 改名後的公開 action 保留 [HttpPost] 與輸入契約。
    /// </summary>
    [TestClass]
    public class ServiceRenameContractTests
    {
        private static MethodInfo? DeclaredPublic(Type type, string name) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        private static MethodInfo? DeclaredPublicSave(Type type, Type parameterType) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == "Save"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == parameterType);

        [TestMethod]
        [DataRow(typeof(WeaponService), typeof(WeaponInstanceModel))]
        [DataRow(typeof(TalentService), typeof(TalentModel))]
        [DataRow(typeof(GameProgressService), typeof(GameProgressModel))]
        [DataRow(typeof(AchievementProgressService), typeof(AchievementProgressModel))]
        public void SnapshotSave_OverridesSharedBase(Type serviceType, Type modelType)
        {
            MethodInfo? save = DeclaredPublicSave(serviceType, modelType);
            Assert.IsNotNull(save);
            Assert.AreNotEqual(save.DeclaringType, save.GetBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(_SnapshotService<,>), save.GetBaseDefinition().DeclaringType!.GetGenericTypeDefinition());
        }

        [TestMethod]
        public void WeaponService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            Assert.IsNotNull(DeclaredPublic(typeof(WeaponService), "GetInstanceList"));
            Assert.IsNotNull(DeclaredPublicSave(typeof(WeaponService), typeof(WeaponInstanceModel)));
            Assert.IsNull(DeclaredPublic(typeof(WeaponService), "GetWeaponList"));
            Assert.IsNull(DeclaredPublic(typeof(WeaponService), "SaveWeapon"));
        }

        [TestMethod]
        public void TalentService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            Assert.IsNotNull(DeclaredPublic(typeof(TalentService), "GetSnapshot"));
            Assert.IsNotNull(DeclaredPublicSave(typeof(TalentService), typeof(TalentModel)));
            Assert.IsNull(DeclaredPublic(typeof(TalentService), "GetTalent"));
            Assert.IsNull(DeclaredPublic(typeof(TalentService), "SaveTalent"));
        }

        [TestMethod]
        public void ItemService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            var inStock = DeclaredPublic(typeof(ItemService), "GetInStockList");
            Assert.IsNotNull(inStock);
            Assert.IsEmpty(inStock.GetParameters());

            var adjust = DeclaredPublic(typeof(ItemService), "AdjustAmount");
            Assert.IsNotNull(adjust);
            var parameters = adjust.GetParameters();
            Assert.HasCount(3, parameters);
            Assert.AreEqual(typeof(string), parameters[0].ParameterType);
            Assert.AreEqual(typeof(int), parameters[1].ParameterType);
            Assert.AreEqual(typeof(string), parameters[2].ParameterType);
            Assert.AreEqual("category", parameters[2].Name);
            Assert.IsTrue(parameters[2].IsOptional);
            Assert.IsNull(parameters[2].DefaultValue);

            Assert.IsNull(DeclaredPublic(typeof(ItemService), "GetItemList"));
            Assert.IsNull(DeclaredPublic(typeof(ItemService), "AddItem"));
        }

        [TestMethod]
        public void GameProgressService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            Assert.IsNotNull(DeclaredPublic(typeof(GameProgressService), "Get"));
            Assert.IsNotNull(DeclaredPublicSave(typeof(GameProgressService), typeof(GameProgressModel)));
            Assert.IsNull(DeclaredPublic(typeof(GameProgressService), "GetProgress"));
            Assert.IsNull(DeclaredPublic(typeof(GameProgressService), "SaveProgress"));
        }

        [TestMethod]
        public void AchievementProgressService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            Assert.IsNotNull(DeclaredPublic(typeof(AchievementProgressService), "Get"));
            Assert.IsNotNull(DeclaredPublicSave(typeof(AchievementProgressService), typeof(AchievementProgressModel)));
            Assert.IsNull(DeclaredPublic(typeof(AchievementProgressService), "GetProgress"));
            Assert.IsNull(DeclaredPublic(typeof(AchievementProgressService), "SaveProgress"));
        }

        [TestMethod]
        public void PlayerService_RenamedMembers_Exist_AndOldNamesRemoved()
        {
            var getIdByToken = DeclaredPublic(typeof(PlayerService), "GetIdByToken");
            Assert.IsNotNull(getIdByToken);
            var tokenParameters = getIdByToken.GetParameters();
            Assert.HasCount(1, tokenParameters);
            Assert.AreEqual(typeof(Guid), tokenParameters[0].ParameterType);

            var findOrCreateGuest = DeclaredPublic(typeof(PlayerService), "FindOrCreateGuest");
            Assert.IsNotNull(findOrCreateGuest);
            var guestParameters = findOrCreateGuest.GetParameters();
            Assert.HasCount(1, guestParameters);
            Assert.AreEqual(typeof(string), guestParameters[0].ParameterType);

            var findOrCreateGoogle = DeclaredPublic(typeof(PlayerService), "FindOrCreateGoogle");
            Assert.IsNotNull(findOrCreateGoogle);
            var googleParameters = findOrCreateGoogle.GetParameters();
            Assert.HasCount(2, googleParameters);
            Assert.AreEqual(typeof(string), googleParameters[0].ParameterType);
            Assert.AreEqual(typeof(string), googleParameters[1].ParameterType);

            var getDataByLogin = DeclaredPublic(typeof(PlayerService), "GetDataByLogin");
            Assert.IsNotNull(getDataByLogin);
            var loginParameters = getDataByLogin.GetParameters();
            Assert.HasCount(1, loginParameters);
            Assert.AreEqual(typeof(LoginRequest), loginParameters[0].ParameterType);

            Assert.IsNull(DeclaredPublic(typeof(PlayerService), "GetPlayerIdByToken"));
            Assert.IsNull(DeclaredPublic(typeof(PlayerService), "FindOrCreateGuestPlayer"));
            Assert.IsNull(DeclaredPublic(typeof(PlayerService), "FindOrCreateGooglePlayer"));
            Assert.IsNull(DeclaredPublic(typeof(PlayerService), "GetPlayerByLogin"));
        }
        [TestMethod]
        [DataRow(typeof(WeaponController), "GetInstanceList")]
        [DataRow(typeof(WeaponController), "Save")]
        [DataRow(typeof(WeaponController), "Delete")]
        [DataRow(typeof(TalentController), "GetSnapshot")]
        [DataRow(typeof(TalentController), "Save")]
        [DataRow(typeof(ItemController), "GetInStockList")]
        [DataRow(typeof(ItemController), "AdjustAmount")]
        [DataRow(typeof(ItemController), "Delete")]
        [DataRow(typeof(GameProgressController), "Get")]
        [DataRow(typeof(GameProgressController), "Save")]
        [DataRow(typeof(AchievementProgressController), "Get")]
        [DataRow(typeof(AchievementProgressController), "Save")]
        [DataRow(typeof(AchievementProgressController), "ClaimReward")]
        [DataRow(typeof(ManagerController), "GetSession")]
        public void Controller_PublicAction_UsesCurrentName_WithHttpPost(Type controllerType, string actionName)
        {
            var action = DeclaredPublic(controllerType, actionName);
            Assert.IsNotNull(action, $"{controllerType.Name}.{actionName} 必須保留為公開 action。");
            Assert.IsNotNull(action.GetCustomAttribute<HttpPostAttribute>(), $"{controllerType.Name}.{actionName} 必須保留 [HttpPost]。");
        }

        [TestMethod]
        [DataRow(typeof(WeaponController), "Save", typeof(WeaponInstanceModel))]
        [DataRow(typeof(WeaponController), "Delete", typeof(EntryKeyRequest))]
        [DataRow(typeof(TalentController), "Save", typeof(TalentModel))]
        [DataRow(typeof(ItemController), "AdjustAmount", typeof(ItemDeltaRequest))]
        [DataRow(typeof(ItemController), "Delete", typeof(ItemKeyRequest))]
        [DataRow(typeof(GameProgressController), "Save", typeof(GameProgressModel))]
        [DataRow(typeof(AchievementProgressController), "Save", typeof(AchievementProgressModel))]
        public void Controller_RenamedAction_PreservesInputContract(Type controllerType, string actionName, Type modelType)
        {
            var action = DeclaredPublic(controllerType, actionName);
            Assert.IsNotNull(action);
            var parameters = action.GetParameters();
            Assert.HasCount(1, parameters);
            Assert.AreEqual(modelType, parameters[0].ParameterType);
            Assert.AreEqual(typeof(AuthorizeApiController), controllerType.BaseType);
        }
    }
}
