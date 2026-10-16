using System.Data.SqlClient;

namespace ReconstructNodesServer.Repositories;

internal static class SqlConcurrency
{
    internal static bool IsRetryable(Exception error) => error is SqlException sql &&
        (sql.Number is 3960 or 1205 or 51006 || sql.Number is 2601 or 2627 &&
            new[] { "IX_EconomyRequest_CreateUserId_RequestId", "IX_PlayerParams_CreateUserId_Group_Name",
                "IX_Item_CreateUserId_ItemKey", "IX_Character_CreateUserId_Kind",
                "IX_Character_CreateUserId_WeaponEntryKey", "IX_Weapon_CreateUserId_EntryKey",
                "IX_Talent_CreateUserId" }
                .Any(index => sql.Message.Contains(index, StringComparison.Ordinal)));
}
