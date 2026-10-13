using System;
using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(EconomyRequest))]
    public class EconomyRequest : ModelBase
    {
        public Guid RequestId { get => Get<Guid>(); set => Set(value); }
        public string Operation { get => Get<string>(); set => Set(value); }
        public string Payload { get => Get<string>(); set => Set(value); }
        public string Result { get => Get<string>(); set => Set(value); }
    }
}
