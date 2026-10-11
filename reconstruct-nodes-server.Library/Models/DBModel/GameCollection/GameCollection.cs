using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace ReconstructNodesServer.Library.DBModel
{
    [Dapper.Contrib.Extensions.Table(nameof(GameCollection))]
    public class GameCollection : ModelBase
    {
        public int Count { get => Get<int>(); set => Set(value); }
    }


}
