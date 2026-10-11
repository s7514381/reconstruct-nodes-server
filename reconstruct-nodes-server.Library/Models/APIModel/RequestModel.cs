using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.Models
{
    public class RequestModel
    {
        public Guid? Id { get; set; }
        public List<Guid> IdList { get; set; } = new List<Guid>();
        public int? PullCount { get; set; }
        public string Value { get; set; }
    }
}
