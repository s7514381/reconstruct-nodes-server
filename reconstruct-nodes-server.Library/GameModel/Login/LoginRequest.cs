using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class LoginRequest
    {
        public ELoginType? Type { get; set; }
        public string ClientId { get; set; }
        public string GoogleId { get; set; }
        public Guid? EmailVerifyId { get; set; }

        private string _email;
        public string Email
        {
            get => _email; 
            set => _email = value?.Trim().Normalize();
        }

        public string EmailVerificationCode { get; set; }
        public bool? ClientIdChangeConfirm { get; set; }
        public string Locale { get; set; }

    }
}
