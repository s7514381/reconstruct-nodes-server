using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Achievement))]
    public class Achievement : _AchievementSetting 
    {
        public float? Total { get => Get<float?>(); set => Set(value); }
        public float? DayTotal { get => Get<float?>(); set => Set(value); }
    }

    /// <summary>成就共用基底欄位（Name、Category），由 Achievement 繼承；舊設定表 AchievementSetting 已移除</summary>
    public class _AchievementSetting : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public int Category { get => Get<int>(); set => Set(value); }
    }
}
