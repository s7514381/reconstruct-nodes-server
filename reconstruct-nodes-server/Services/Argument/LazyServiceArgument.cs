
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
        public Lazy<AchievementDetailService> AchievementDetailService { get; set; }
        public Lazy<EmailVerifyService> EmailVerifyService { get; set; }
        public Lazy<SystemConfigService> SystemConfigService { get; set; }
        public Lazy<AzureStoreService> AzureStoreService { get; set; }
        public Lazy<ManyToManyService> ManyToManyService { get; set; }
        public Lazy<AchievementService> AchievementService { get; set; }
        public Lazy<PlayerParamsService> PlayerParamsService { get; set; }
        public Lazy<WeaponService> WeaponService { get; set; }
        public Lazy<GoldCommand> GoldCommand { get; set; }
        //
        public LazyServiceArgument(
            Lazy<MailSender> mailSender,
            Lazy<BaseService> baseService,
            Lazy<AchievementDetailService> achievementDetailService,
            Lazy<PlayerService> playerService,
            Lazy<EmailVerifyService> emailVerifyService,
            Lazy<SystemConfigService> systemConfigService,
            Lazy<AzureStoreService> azureStoreService,
            Lazy<ManyToManyService> manyToManyService,
            Lazy<AchievementService> achievementService,
            Lazy<PlayerParamsService> playerParamsService,
            Lazy<WeaponService> weaponService,
            Lazy<GoldCommand> goldService)
        {
            MailSender = mailSender;
            BaseService = baseService;
            PlayerService = playerService;
            AchievementDetailService = achievementDetailService;
            EmailVerifyService = emailVerifyService;
            SystemConfigService = systemConfigService;
            AzureStoreService = azureStoreService;
            ManyToManyService = manyToManyService;
            AchievementService = achievementService;
            PlayerParamsService = playerParamsService;
            WeaponService = weaponService;
            GoldCommand = goldService;
        }

    }
}