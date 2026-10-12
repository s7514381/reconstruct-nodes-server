# reconstruct-nodes-server

ASP.NET Core（net10.0）後端服務，提供 **reconstruct-nodes** 遊戲前端使用的 API。

## 前端專案

前端是獨立倉庫 **`reconstruct-nodes`**（Godot 專案），與本後端分開維護，透過 HTTP 呼叫本後端的 API。本文件以本倉庫實作為準，不涵蓋前端倉庫內的設定與驗證。

## 專案結構

| 路徑 | 說明 |
| --- | --- |
| `reconstruct-nodes-server/` | ASP.NET Core Web 專案（net10.0；Controllers、Services、Repositories、Migrations） |
| `reconstruct-nodes-server.Library/` | 共用 DB 模型、遊戲模型、API DTO 與工具（net10.0） |
| `docs/ARCHITECTURE.md` | 分層架構與 `_ModelService` / `BaseServiceApiController` 設計說明 |

## 資料流

```
reconstruct-nodes（前端）→ HTTP → Controller → Service → BaseService → IDapperRepository → Dapper → SQL Server
```

詳細說明請見 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 本機啟動

需要 .NET 10 SDK 與可連線的 SQL Server。先設定資料庫連線及下節的必要憑證：

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL Server connection string>" --project .\reconstruct-nodes-server
dotnet build .\reconstruct-nodes-server.sln
dotnet run --project .\reconstruct-nodes-server --launch-profile https
```

`https` profile 使用 `Development`，網址為 `https://localhost:7007` 與
`http://localhost:5073`；Swagger UI 位於 `/swagger`，僅在 Development 啟用。
若本機 HTTPS 憑證尚未受信任，可執行 `dotnet dev-certs https --trust`。

程式不會在啟動時自動套用 EF Core 遷移。需要更新資料庫時，先確認連線目標、
備份與遷移內容；`SplitSavesIntoDomainTables` 會刪除舊 `PlayerSave`，
不搬移其資料。確認可套用後才執行：

```powershell
Push-Location .\reconstruct-nodes-server
try {
    dotnet tool restore
    dotnet ef database update -- --environment Development
} finally {
    Pop-Location
}
```

部署時以平台秘密設定或 `ConnectionStrings__DefaultConnection` 提供連線字串。
本節命令是操作說明，不代表文件同步時已執行建置或資料庫更新。

## API 概覽

- 裝置登入：`POST DeviceAuth/Login`。
- 管理端登入與 session：`POST Manager/Login`、`POST Manager/GetSession`（原 `GetManagerSession`，已改名，見 docs/ARCHITECTURE.md 10.2 的 URL 改名表）。
- 遊戲資料：`GameProgress`、`Talent`、`Item`、`Weapon`、`AchievementProgress`。
- 通用 CRUD 與舊 `/Login` 保留原有回應格式，不全部使用 `BackendApiResult<T>`。
- 繼承 `BaseApiController` 的 API 在非 DEBUG 組建檢查 `Encode`；
  `ManagerController` 不套用此 Filter。授權 API 使用原始 GUID Token，
  不是 Bearer 格式。詳見 [API 規則](docs/ARCHITECTURE.md#101-共通規則)。

## 本機與部署憑證設定

請勿將服務帳戶 JSON、Azure 存取金鑰或 SMTP 密碼提交至 Git。共用的
`appsettings.json` 只保留非秘密設定。

開發環境（`Development`）透過 ASP.NET Core 預設設定載入 .NET User Secrets：

```powershell
dotnet user-secrets set "AzureStorage:ConnectionString" "<Azure connection string>" --project .\reconstruct-nodes-server
dotnet user-secrets set "SmtpConfig:Password" "<SMTP app password>" --project .\reconstruct-nodes-server
```

以上為佔位範例；不要將實際秘密貼入共享終端或紀錄。User Secrets 不加密，只適合本機開發。
部署時使用平台秘密設定或環境變數 `AzureStorage__ConnectionString`、
`SmtpConfig__Password`，不使用開發電腦的 User Secrets。
Azure 或 SMTP 服務建立時若缺少上述設定，會明確拋出設定錯誤。

Firebase 使用 Google Application Default Credentials。將服務帳戶 JSON 放在
倉庫外並設定 `GOOGLE_APPLICATION_CREDENTIALS` 為檔案的絕對路徑：

```powershell
$env:GOOGLE_APPLICATION_CREDENTIALS = "$env:LOCALAPPDATA\reconstruct-nodes-server\credentials\firebase-adminsdk.json"
dotnet run --project .\reconstruct-nodes-server
```

Firebase Admin、Firestore 與 Realtime Database 共用此憑證，保留原有專案與資料庫。
未提供可用的 ADC 時，Google SDK 會明確報錯，不回退至倉庫內的檔案。
若設定 Windows 使用者層級環境變數，請重新啟動 Visual Studio／終端以載入。
部署環境也可使用平台提供且具備適當權限的 ADC 身分，無需提交服務帳戶私鑰。

### 已提交秘密的處理

若 GitHub 以 `GH013 / GITHUB PUSH PROTECTION` 拒絕推送，必須從所有待推送提交
移除秘密；只新增刪除秘密的提交或 `.gitignore` 不會清除舊歷史。
只有在確認提交尚未分享且取得同意後，才改寫該提交，不繞過 Push Protection。

已提交的憑證應視為需要輪替，清理 Git 並不會撤銷它們：

1. Google Cloud IAM：建立替代服務帳戶金鑰、更新倉庫外憑證並驗證，再刪除舊金鑰。
2. Azure Storage：使用另一組有效金鑰切換服務連線，再重新產生已暴露的金鑰；
   同步更新所有使用者與部署環境，避免中斷其他服務。
3. Google 帳戶：撤銷舊 SMTP 應用程式密碼，建立替代密碼並更新 User Secrets／部署秘密。

輪替需由具備雲端管理權限的人員執行。`git push --dry-run` 不會完整驗證秘密掃描，
只有實際推送被 GitHub 接受才能確認已解除推送阻擋。
