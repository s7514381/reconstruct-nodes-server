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
| `ServerClientId` | Web（server）OAuth Client ID | 可由 `Secret/google-web.json` 的 `web.client_id` 提供；Android 的 audience，未設定時 `LoginIdToken` 回 503 |
| `ClientSecret` | 桌面 OAuth Client Secret | 可由 Git 忽略的 `Secret/google-desktop.json`、user-secrets 或環境變數 `GoogleAuth__ClientSecret` 提供。不得寫入受版本控制的檔案或客戶端。`Login` 需要；`LoginIdToken` 不使用 |
| `TokenEndpoint` | 預設 `https://oauth2.googleapis.com/token` | |
| `JwksEndpoint` | 預設 `https://www.googleapis.com/oauth2/v3/certs` | ID Token 簽章公鑰 |
| `ClockSkewSeconds` | 預設 60 | 時間驗證容許偏移 |
| `JwksCacheMinutes` | 預設 60 | 公鑰快取時間 |

兩條路徑互相獨立：只設定 `ServerClientId` 即可啟用 Android，不需要桌面的 secret；反之亦然。

### 獨立設定檔與發布

在後端專案根目錄建立 `Secret` 資料夾，將 Google Cloud Console 下載的
**Desktop app OAuth 用戶端 JSON** 複製進去，固定命名為 `google-desktop.json`，
不需要手動修改 Google 下載的內容：

```powershell
New-Item -ItemType Directory -Force .\reconstruct-nodes-server\Secret
Copy-Item "<下載的 Desktop OAuth JSON 路徑>" .\reconstruct-nodes-server\Secret\google-desktop.json
```

```json
{
  "installed": {
    "client_id": "<Desktop OAuth Client ID>",
    "client_secret": "<Desktop OAuth Client Secret>"
  }
}
```

上例僅示意必要欄位，可保留原始下載檔案的其他欄位。
程式只將 `installed.client_id`、`installed.client_secret` 映射至
`GoogleAuth:ClientId`、`GoogleAuth:ClientSecret`；不從檔案讀取 endpoint，
也不把桌面 ID 當成 Android 的 `ServerClientId`。

Android 登入另將下載的 **Web application OAuth JSON** 放入
`Secret/google-web.json`，程式僅將 `web.client_id` 映射為 `GoogleAuth:ServerClientId`。
Web 憑證的 `client_secret` 不載入、不用於 Android ID Token 驗證，也不要求存在。
Android 客戶端 Credential Manager 請使用同一個 Web client ID 作為 server client ID。

Android OAuth 原始檔可另保存在 `Secret/google-android.json`；
後端不載入、不發布該檔案，也不以其 client ID 驗證 Android ID Token。

整個 `Secret` 資料夾由 `.gitignore` 忽略，但**只有 `google-desktop.json` 與 `google-web.json`**
會隨建置與 `dotnet publish .\reconstruct-nodes-server -c Release` 複製，
並保留 `Secret` 子目錄。部署完整發布目錄後，程式從 content root 載入該檔案，
不需要在正式機另設 Google 環境變數。CI 或其他電腦需在發布前自行提供此檔案，
Git clone 不會取得你的本機設定。

優先序由低至高：`appsettings.json`、環境 appsettings、
`Secret/google-desktop.json`／`Secret/google-web.json`（各提供不同鍵）、
Development User Secrets、環境變數、命令列。
缺少檔案仍沿用原本設定；缺少對應 Google 登入設定時維持既有 503 回應。
JSON 格式錯誤、桌面檔不是 `installed`／Web 檔不是 `web` 格式或必要欄位缺漏會使啟動失敗；
修改設定後需重啟程式。發布時請使用乾淨的發布目錄，避免舊憑證殘留。

**Git 忽略不代表加密**：此檔案在本機、發布產物及正式機都是明文。
限制檔案及備份的存取權限，不可放在 `wwwroot`、公開下載位置或客戶端，
也不要將含憑證的發布產物上傳至公開儲存空間。

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
2. 填寫獨立設定檔或使用部署秘密設定，提供 `ClientId` 與 `ClientSecret`（桌面登入）或 `ServerClientId`（Android 登入），並確認發布產物包含所需設定。不得把憑證提交到 git。
3. 執行遷移，並確認索引 `UX_Player_GoogleId_NonClient` 存在。