using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Achievement))]
    public class Achievement : _AchievementSetting 
    {
        [Relation(nameof(AchievementSetting))]
        public Guid? AchievementSettingId { get => Get<Guid?>(); set => Set(value); }
        public float? Total { get => Get<float?>(); set => Set(value); }
        public float? DayTotal { get => Get<float?>(); set => Set(value); }
    }

}
