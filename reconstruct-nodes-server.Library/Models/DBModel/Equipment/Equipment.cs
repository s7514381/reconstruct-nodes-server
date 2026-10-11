using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace ReconstructNodesServer.Library.DBModel
{
    [Dapper.Contrib.Extensions.Table(nameof(Equipment))]
    public class Equipment : ModelBase
    {
        public EquipmentCategory Category { get => Get<EquipmentCategory>(); set => Set(value); }
        public bool IsEquipped { get => Get<bool>(); set => Set(value); }
        public bool IsLock { get => Get<bool>(); set => Set(value); }
    }

    public enum EquipmentCategory
    {
        Clothing_1 = 1,
        Clothing_2 = 2,
        Clothing_3 = 3,
        Clothing_4 = 4,
        Clothing_5 = 5,
        Clothing_6 = 6,
        Clothing_7 = 7,
        Clothing_8 = 8,
        Clothing_9 = 9,
        Clothing_10 = 45,
        Head_1 = 10,
        Head_2 = 11,
        Head_3 = 12,
        Head_4 = 13,
        Head_5 = 14,
        Head_6 = 15,
        Head_7 = 16,
        Head_8 = 17,
        Head_9 = 18,
        Gloves_1 = 19,
        Gloves_2 = 20,
        Gloves_3 = 21,
        Gloves_4 = 22,
        Gloves_5 = 23,
        Gloves_6 = 24,
        Gloves_7 = 25,
        Gloves_8 = 26,
        Gloves_9 = 27,
        Shoes_1 = 28,
        Shoes_2 = 29,
        Shoes_3 = 30,
        Shoes_4 = 31,
        Shoes_5 = 32,
        Shoes_6 = 33,
        Shoes_7 = 34,
        Shoes_8 = 35,
        Shoes_9 = 36,
        Accessories_1 = 37,
        Accessories_2 = 38,
        Accessories_3 = 39,
        Accessories_4 = 40,
        Accessories_5 = 41,
        Accessories_6 = 42,
        Accessories_7 = 43,
        Accessories_8 = 44,
    }

}
