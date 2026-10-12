# Google 登入（後端）

客戶端說明見 `reconstruct-nodes/docs/google-login.md`。

## 端點

| 端點 | 用途 | 請求 | 驗證方式 |
| --- | --- | --- | --- |
| `POST GoogleAuth/Login` | Windows／桌面 PKCE 授權碼流程 | `{Code, CodeVerifier, RedirectUri, Locale}` | 以 `ClientId` + `ClientSecret` 向 Google token endpoint 交換授權碼，驗證回傳的 ID Token，audience 為 `ClientId` |
| `POST GoogleAuth/LoginIdToken` | Android Credential Manager | `{IdToken, Locale}` | 直接驗證客戶端送來的 ID Token，audience 為 `ServerClientId`；不需要 `ClientSecret` |

兩個端點成功時回傳 `BackendApiResult<BackendLoginData>`，`Data` 為 `{Token, Name}`。`Token` 是新建立的 session GUID，之後以 `Authorization` header 傳入。

## 設定（`GoogleAuth` 區段）

| 鍵 | 用途 | 規則 |
| --- | --- | --- |
| `ClientId` | 桌面 OAuth Client ID（公開值） | 桌面流程的 audience；未設定時 `Login` 回 503 |
| `ServerClientId` | Web（server）OAuth Client ID | Android 的 audience；未設定時 `LoginIdToken` 回 503 |
| `ClientSecret` | 桌面 OAuth Client Secret | **只允許** user-secrets 或環境變數 `GoogleAuth__ClientSecret`。不得寫入 appsettings、git 或客戶端。`Login` 需要；`LoginIdToken` 不使用 |
| `TokenEndpoint` | 預設 `https://oauth2.googleapis.com/token` | |
| `JwksEndpoint` | 預設 `https://www.googleapis.com/oauth2/v3/certs` | ID Token 簽章公鑰 |
| `ClockSkewSeconds` | 預設 60 | 時間驗證容許偏移 |
| `JwksCacheMinutes` | 預設 60 | 公鑰快取時間 |

兩條路徑互相獨立：只設定 `ServerClientId` 即可啟用 Android，不需要桌面的 secret；反之亦然。

## 錯誤回應

| 情況 | HTTP | 訊息（Data 為空） |
| --- | --- | --- |
| 對應的 client ID 未設定 | 503 | `Google 登入尚未設定，請使用訪客登入` |
| 請求格式錯誤（缺 code／token，或 ID Token 超過 8192 字元） | 400 | 依例外訊息 |
| 授權碼被 Google 拒絕（`Login`） | 401 | 依例外訊息 |
| ID Token 簽章、audience、issuer、時效驗證失敗 | 401 | `Google 身分驗證失敗，請重新登入` |
| 連線 Google token／JWKS 失敗 | 502 | `無法連線 Google 驗證服務，請稍後再試` |
| 其他未預期錯誤 | 500 | `登入失敗，請稍後再試` |

這些狀態碼由 `GoogleAuthController.MapFailure` 統一對應，兩個端點共用。

## 帳號綁定規則

- Google 玩家以 `Player.GoogleId`（Google `sub`）對應，查詢條件為 `GoogleId = @GoogleId AND IsClientLogin = 0`（`PlayerService.GetDataByLogin`）。
- **訪客列（`IsClientLogin = 1`）永遠不會被 Google 登入查到**，因此 Google 登入不會遷移或覆寫訪客進度。
- 非訪客列上既有的 `GoogleId` 會被綁定：同一 `sub` 再次登入會回到同一玩家。
- 客戶端在 `Session/Validate` 取得的 `LoginKind` 以 `IsClientLogin` 為權威來源：`guest`、`google`（非訪客且 `GoogleId` 非空）、`other`。

## 資料庫與併發

- 遷移 `20261012064533_GooglePlayerGoogleIdUnique`：
  - `Player.GoogleId` 由 `nvarchar(max)` 改為 `nvarchar(128)`（SQL Server 無法對 `nvarchar(max)` 建立索引）。
  - 建立篩選式唯一索引 `UX_Player_GoogleId_NonClient`，條件 `[GoogleId] IS NOT NULL AND [IsClientLogin] = 0`。
- **遷移前檢查**：若已有重複的非訪客 `GoogleId`，或任一 `GoogleId` 超過 128 字元，遷移會以 `THROW 50000` 中止，**不會自動刪除或改寫玩家資料**。需先人工合併或處理，再重新執行遷移。
- 併發首次登入：`PlayerService.FindOrCreateGoogle` 的 INSERT 若遇到 2601／2627（唯一鍵衝突），會改讀同一 `GoogleId` 的已提交列並回傳，確保同一 `sub` 只對應一筆玩家。
- 是否 `XACT_ABORT` 為 OFF 目前未在正式環境驗證；此程式假設語句級錯誤不會中止外層交易。

## 日誌與敏感資料

- `APIRequest` 中介層對 `/GoogleAuth/*` 的請求內容記錄為 `[REDACTED]`（授權碼、verifier、ID Token 不寫入 Azure 日誌）。
- 其他 POST 請求，若 `Authorization` 快取未命中，原始 session GUID 不寫入日誌，只記錄 `[REDACTED]`；快取命中時只記錄玩家 Id。
- 回應內容不記錄（中介層只記錄請求 Body 與 Path）。
- 控制器的登入失敗 log 只包含例外訊息與型別，不包含 Token 或 ID Token。

## 測試

```powershell
dotnet test reconstruct-nodes-server.Tests --filter FullyQualifiedName~Auth
```

測試皆為 mock（`FakeValidator`、`FakeCodeExchanger`、`FakeSessionStore`），不連線 Google，也不連線資料庫。

## 真實驗證範圍與限制

- **Windows 真實 Google 授權碼流程已驗證**：使用真實 Desktop OAuth client 與後端 User Secrets 完成授權碼交換、ID Token 驗證及 Google session 建立，Godot 正式入口成功進入戰鬥；`Session/Validate` 回傳 `google`，客戶端同一隔離環境重啟後可恢復登入。此結果不涵蓋 Android ID Token 入口。
- **真實 Android 未驗證**：`LoginIdToken` 只以 mock 測試 audience 與錯誤路徑。Credential Manager 的實際 ID Token audience 尚未與真實 Web client ID 核對。
- **Google `sub` 跨 client 穩定性未驗證**：本實作假設同一 Google 帳號的 `sub` 在桌面與 Android 的 ID Token 中相同（由 Google 文件推定，未實測）。若不成立，同一人會得到兩個玩家。
- **Google 遷移已在開發 SQL Server 執行**：先唯讀確認沒有重複或過長的 GoogleId，再經使用者授權套用 `GooglePlayerGoogleIdUnique`，欄位調整與篩選式唯一索引建立成功；不代表含非法資料時的 `THROW` 分支或正式資料庫遷移已驗證。
- **併發未以實際多執行緒壓力測試**：競爭處理只有程式審查，沒有 DB 整合測試。

## 部署前必做

1. 在正式庫執行重複與長度檢查 SQL，若有結果先人工處理。
2. 設定 `ClientSecret`（若要使用桌面登入）與 `ServerClientId`（若要使用 Android 登入）。不得把任何值提交到 git。
3. 執行遷移，並確認索引 `UX_Player_GoogleId_NonClient` 存在。