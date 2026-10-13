using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Item))]
    public class Item : ModelBase
    {
        public string ItemKey { get => Get<string>(); set => Set(value); }

        public int Amount { get => Get<int>(); set => Set(value); }

        public string Category { get => Get<string>(); set => Set(value); }
    }
}
