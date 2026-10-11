
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;


namespace ReconstructNodesServer.Argument
{
    public class LazyServiceArgument
    {
        public Lazy<MailSender> MailSender { get; set; }
        public Lazy<BaseService> BaseService { get; set; }
        public Lazy<PlayerService> PlayerService { get; set; }
        public Lazy<CharacterService> CharacterService { get; set; }
        public Lazy<AbilityCardService> AbilityCardService { get; set; }
        public Lazy<ModifierService> ModifierService { get; set; }
        public Lazy<EquipmentService> EquipmentService { get; set; }
        public Lazy<SettingVersionService> SettingVersionService { get; set; }
        public Lazy<AchievementDetailService> AchievementDetailService { get; set; }
        public Lazy<AchievementDetailSettingService> AchievementDetailSettingService { get; set; }
        public Lazy<GameCollectionService> GameCollectionService { get; set; }
        public Lazy<EmailVerifyService> EmailVerifyService { get; set; }
        public Lazy<SystemConfigService> SystemConfigService { get; set; }
        public Lazy<AzureStoreService> AzureStoreService { get; set; }
        public Lazy<ManyToManyService> ManyToManyService { get; set; }
        public Lazy<AbilitySelectService> AbilitySelectService { get; set; }
        public Lazy<MapService> MapService { get; set; }
        public Lazy<AchievementService> AchievementService { get; set; }
        public Lazy<AbilityService> AbilityService { get; set; }
        public Lazy<FirebaseService> FirebaseService { get; set; }
        public Lazy<MonsterSettingService> MonsterSettingService { get; set; }
        public Lazy<PlayerParamsService> PlayerParamsService { get; set; }
        public Lazy<WeaponService> WeaponService { get; set; }
        //
        public LazyServiceArgument(
            Lazy<MailSender> mailSender,
            Lazy<BaseService> baseService,
            Lazy<CharacterService> characterService,
            Lazy<AbilityCardService> abilityCardService,
            Lazy<ModifierService> modifierService,
            Lazy<EquipmentService> equipmentService,
            Lazy<SettingVersionService> settingVersionService,
            Lazy<AchievementDetailService> achievementDetailService,
            Lazy<AchievementDetailSettingService> achievementDetailSettingService,
            Lazy<PlayerService> playerService,
            Lazy<GameCollectionService> gameCollectionService,
            Lazy<EmailVerifyService> emailVerifyService,
            Lazy<SystemConfigService> systemConfigService,
            Lazy<AzureStoreService> azureStoreService,
            Lazy<ManyToManyService> manyToManyService,
            Lazy<AbilitySelectService> abilitySelectService,
            Lazy<MapService> mapService,
            Lazy<AchievementService> achievementService,
            Lazy<AbilityService> abilityService,
            Lazy<FirebaseService> firebaseService,
            Lazy<PlayerParamsService> playerParamsService,
            Lazy<MonsterSettingService> monsterSettingService,
            Lazy<WeaponService> weaponService)
        {
            MailSender = mailSender;
            BaseService = baseService;
            PlayerService = playerService;
            CharacterService = characterService;
            AbilityCardService = abilityCardService;
            ModifierService = modifierService;
            EquipmentService = equipmentService;
            SettingVersionService = settingVersionService;
            AchievementDetailService = achievementDetailService;
            AchievementDetailSettingService = achievementDetailSettingService;
            GameCollectionService = gameCollectionService;
            EmailVerifyService = emailVerifyService;
            SystemConfigService = systemConfigService;
            AzureStoreService = azureStoreService;
            ManyToManyService = manyToManyService;
            AbilitySelectService = abilitySelectService;
            MapService = mapService;
            AchievementService = achievementService;
            AbilityService = abilityService;
            FirebaseService = firebaseService;
            MonsterSettingService = monsterSettingService;
            PlayerParamsService = playerParamsService;
            WeaponService = weaponService;
        }

    }
}