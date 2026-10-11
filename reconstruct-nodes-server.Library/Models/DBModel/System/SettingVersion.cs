using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(SettingVersion))]
    public class SettingVersion : ModelBase
    {
        public string TableName { get => Get<string>(); set => Set(value); }
        public string Version { get => Get<string>(); set => Set(value); }
    }
}
