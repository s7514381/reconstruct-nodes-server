# Email 登入

端點（`BaseApiController`，`[controller]/[action]`，不需 Token）：

| 端點 | 請求 | 回應 |
| --- | --- | --- |
| `POST EmailAuth/Login` | `Email`、`ClientId`（裝置 ID）、`Locale` | 200 `EmailLoginData`：可信裝置含 `Token`/`Name`；否則 `VerificationRequired=true`、`ExpiresInSeconds`、`ResendAfterSeconds` 並寄出驗證碼 |
| `POST EmailAuth/Verify` | `Email`、`ClientId`、`Code`（6 位數字）、`Locale` | 200 `EmailLoginData`（含 `Token`/`Name`） |

## 規則

- 信箱 trim＋小寫正規化（≤254、`MailAddress` 與 `EmailUtility` 驗證）；`ClientId` 16–64 字元英數或 `-`，否則 400 `backend.auth.device_required`。
- **可信裝置**：Email 玩家（`Email` 相符、`IsClientLogin=0`）的 `ClientId` 與請求的裝置 ID 相同（固定時間比較）才直接簽發 Token。新信箱或裝置不同一律寄驗證碼。
- 驗證成功才 `FindOrCreateEmail`：建立／取得玩家並**把 `ClientId` 改為目前裝置**（換裝置後舊裝置需重新驗證），重置 Token（舊 Token 失效）。Email 玩家與訪客、Google 玩家為獨立列，不遷移進度。
- 驗證碼：`RandomNumberGenerator` 6 位數，只存加鹽 SHA-256；10 分鐘有效、單次有效、同信箱只保留最新一組、綁定要求時的裝置 ID、最多 5 次嘗試（`EmailChallengeStore`，記憶體）。
- 寄送節流（429 `backend.auth.email_rate_limited`，參數 `seconds`）：同信箱 60 秒冷卻、同信箱每小時 5 封、同裝置每小時 10 封。寄信失敗（`MailSender.SendOrThrowAsync`）會撤回挑戰與計數並回 503 `backend.auth.email_unavailable`。
- 驗證錯誤：400 格式錯、401 `email_code_invalid`／`email_code_expired`、429 `email_too_many_attempts`。
- `Session/Validate` 對 Email 玩家回報 `LoginKind = "email"`。
- 舊 `Login/Index` 的 `Type=Email` 一律 400 `backend.auth.email_required`（舊 EmailVerify 流程驗證碼可暴力猜測）。

## 設定與驗證

SMTP 沿用 `Secret/smtp.json`（`SmtpConfig`）。測試：`dotnet test --filter EmailChallengeStoreTests`（挑戰、節流、過期、次數、裝置綁定）。路由可用 `curl` 對本機後端送不合法請求驗證 400／401，不會寄信。
