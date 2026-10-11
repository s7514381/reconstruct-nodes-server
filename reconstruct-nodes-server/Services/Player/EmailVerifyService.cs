
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class EmailVerifyService(BaseServiceArgument argument) : _ModelService<EmailVerify, EmailVerifyModel>(argument)
    {
        public async Task<EmailVerify?> GetVerifyDataByEmail(string email)
        {
            string sql = $@"select top 1 *
                              from EmailVerify
                             where Email = @Email
                               and IsVerification = @IsVerification";
            var result = await QueryFirstAsync<EmailVerify>(sql, new { email = email, IsVerification = true });
            return result;
        }

        public async Task VerificationSuccess(Guid id)
        {
            string sql = $@"UPDATE EmailVerify
                               SET IsVerification = 1
                             WHERE Id = @Id";
            var result = await ExecuteAsync(sql, new { Id = id });
        }

        public async Task<bool> Verification(Guid id, string verificationCode)
        {
            string sql = $@"select count(*)
                              from EmailVerify
                             where Id = @Id
                               and VerificationCode = @VerificationCode";
            var count = await QueryFirstAsync<int>(sql, new { Id = id, VerificationCode = verificationCode });
            return count > 0;
        }

        public async Task<(Player, ResultModel<LoginResponse>)> LoginEmailVerify(Player player, Guid? emailVerifyId) 
        {
            ResultModel<LoginResponse> result = new();
            result.Data = new();

            if (!emailVerifyId.HasValue)
            {
                result.Data.Type = LoginResponseType.驗證信箱;
            }
            else 
            {
                EmailVerify emailVerify = await GetData(emailVerifyId.Value);

                if (!emailVerify.VerificationDeadline.HasValue
                    || emailVerify.VerificationDeadline < DateTime.Now)
                {
                    result.Data.Type = LoginResponseType.驗證碼已過期;
                }

                if (emailVerify.IsVerification)
                {
                    player.EmailVerifyId = emailVerifyId;
                    player.Email = emailVerify.Email;

                    emailVerify.VerificationDeadline = null;
                    await UpdateAsync(emailVerify);

                    result.IsSuccess = true;
                }
                else { result.Data.Type = LoginResponseType.驗證碼錯誤; }
            }
            return (player, result);
        }


    }
}
