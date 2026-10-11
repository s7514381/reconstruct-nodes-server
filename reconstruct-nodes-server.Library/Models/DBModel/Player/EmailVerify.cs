using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(EmailVerify))]
    public class EmailVerify : ModelBase
    {
        public string Email { get => Get<string>(); set => Set(value); }
        public string VerificationCode { get => Get<string>(); set => Set(value); }
        public DateTime? VerificationDeadline { get => Get<DateTime?>(); set => Set(value); }
        public bool IsVerification { get => Get<bool>(); set => Set(value); }

    }
}
