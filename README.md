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

`https` profile 使用 `Development`，網址為 `https://localhost:7007`，與遊戲前端預設連線一致。
`http` profile 使用 `http://localhost:7007`，僅供明確選用 HTTP 的開發情境；兩個 profile 不可同時啟動佔用相同埠。
Swagger UI 位於 `/swagger`，僅在 Development 啟用。
若本機 HTTPS 憑證尚未受信任，可執行 `dotnet dev-certs https --trust`。

程式不會在啟動時自動套用 EF Core 遷移。需要更新資料庫時，先確認連線目標、
備份與遷移內容；`SplitSavesIntoDomainTables` 會刪除舊 `PlayerSave`，
不搬移其資料；`DropLegacySettingAndCompleteTables` 會刪除 16 張舊設定與完整資料鏈表，以及 `Achievement`／`AchievementDetail` 的舊設定 Id 欄位，表內資料不保留。`SeparateCharactersFromDeployments` 會清除舊部署、角色装備、部署技能與角色分類的 Item，建立獨立擁有角色；保留武器庫存、金幣與其他進度，但不能恢復被清除資料，且須配合新版前端契約。確認可套用後才執行：

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

## Dapper 資料庫併發上限

`appsettings.json` 的 `DatabaseConcurrency:MaxConnections` 與
`DatabaseConcurrency:MaxOperations` 分別限制開啟的 Dapper 連線數及正在執行的 DB
操作數，預設均為 **10**。兩者必須為正整數，無效設定會在啟動時失敗。
部署可使用 `DatabaseConcurrency__MaxConnections` 與
`DatabaseConcurrency__MaxOperations` 覆寫；變更設定後需重啟程序。

兩個 limiter 為 singleton，額度由同一程序內所有 Repository 共用，
不跨服務副本，不控制 EF Core Migration，也不取代 SqlClient 原生連線池。
已移除原本依回應時間自動調整 semaphore 的機制。

非交易操作完成即關閉連線並歸還額度；交易持有連線直到交易 Dispose。
同一 scoped Repository 的操作會序列化，避免並行使用相同連線／交易。
交易等待下一個命令時不佔用操作額度。

`QueryMultiple`／`QueryMultipleAsync` 回傳 `ManagedGridReader`，提供 buffered
`Read<T>`、`ReadAsync<T>` 與 First／Single 系列讀取方法。必須用 `using` 或
`await using` 釋放 reader（即使已讀完），其生命週期內持有操作、連線及 Repository
互斥額度；在釋放前不要對同一 Repository 執行另一個命令。
讀取失敗會自動釋放並傳遞例外。Repository Dispose 亦會清理未釋放的 reader／交易。
`Connection` 僅供底層診斷，相容保留此屬性；直接開啟連線或執行 ADO.NET
命令會繞過 limiter，應一律使用 Repository 方法。`Transaction` 改為唯讀，
由 `OpenTransaction` 與 `ManagedTransaction` 管理，不能外部指派。

### 真實 SQL Server 整合測試

測試位於 `reconstruct-nodes-server.Tests/Repositories/SqlDatabaseConcurrencyTests.cs`。
一般測試不會連線真實 SQL Server；未明確啟用的 `SqlIntegration` 案例會顯示 skipped。
執行前請確認連線目標是允許測試的資料庫：

```powershell
$env:DB_CONCURRENCY_SQL_TESTS = "1"
$config = Get-Content .\reconstruct-nodes-server\appsettings.json -Encoding UTF8 -Raw | ConvertFrom-Json
$env:DB_CONCURRENCY_SQL_CONNECTION = $config.ConnectionStrings.DefaultConnection
dotnet test .\reconstruct-nodes-server.Tests\reconstruct-nodes-server.Tests.csproj --filter "TestCategory=SqlIntegration"
```

亦可直接將測試資料庫連線字串提供給 `DB_CONCURRENCY_SQL_CONNECTION`，
不要將憑證提交到倉庫。測試需要建表、刪除專用表及觀測 SQL Server DMV 的權限；
啟用後連線、權限或清理失敗都會使測試失敗，不會退回 mock。
測試建立隨機命名的 `dbo.DbConcurrencyTests_*` 專用表並於結束刪除／確認，
不寫入業務資料、不執行 Migration；Contrib 使用交易內的 local temporary table。
若測試程序被強制終止，finally 可能無法執行，須檢查該次專用測試表是否殘留。

涵蓋同步／非同步 CRUD、Contrib identity、MARS 開啟與關閉、Commit／Rollback／
Dispose、reader 生命週期、SQL／連線失敗後恢復，以及每批 12 個工作、三輪重複的
小上限併發測試。峰值來自實際 Open 連線事件及 SQL Server 活動 requests，
不把連線池的 sleeping session 當成開啟連線數；另驗證 4 條交易連線與
2 個操作額度可獨立生效。

若執行中的開發伺服器鎖住建置輸出，可指定獨立的 `OutputPath`；
不要為了測試停止其他人的服務。測試完成後清除本 shell 的 opt-in 設定：

```powershell
Remove-Item Env:\DB_CONCURRENCY_SQL_TESTS
Remove-Item Env:\DB_CONCURRENCY_SQL_CONNECTION
```

## API 概覽

- 固定角色與道具名稱由遊戲翻譯資源提供，後端不保存或回傳 `Character.Name`、`Item.Name`、角色 DTO 的 `name` 或抽卡的 `character_name`；商店 Catalog 不提供名稱。前端以 `kind`／`ItemKey`／商店識別碼取得顯示文字，不再支援角色改名。
- `RemoveStoredGameNames` migration 刪除上述兩個 DB 欄位，原有名稱無法由回滾恢復；部署須同步更新前端契約。`EconomyRequest.Result` 保留交易冪等紀錄，舊 JSON 即使含 `character_name` 也不再由新版抽卡 DTO 回傳；不清除其他交易資料。

- 裝置登入：`POST DeviceAuth/Login`。
- 管理端登入與 session：`POST Manager/Login`、`POST Manager/GetSession`（原 `GetManagerSession`，已改名，見 docs/ARCHITECTURE.md 10.2 的 URL 改名表）。
- 遊戲資料：`Shop`、`Character`、`GameProgress`、`Talent`、`Item`、`Weapon`、`AchievementProgress`。
- `Character/GetList`、`Character/Get?id=<GUID>`、`Character/Save` 提供獨立角色配置；角色未部署也能裝備武器與技能樹。部署只使用 `character_id` 引用角色目前配置，進度覆寫不刪除角色。
- 只有 `Shop/Draw` 能建立擁有角色：單抽 100 金幣、十連 900，新角色配發普通基礎武器，重複發碎片。`Character/Save` 不能建立角色。抽卡需穩定 `request_id`，重送不重複扣款。
- 金幣仍由 `PlayerParams` 保存（`Group = game_progress`、`Name = gold`）。`GoldService` 只負責後端金幣交易，不使用獨立金幣表；`GameProgress/Save` 不採用請求的 `gold`，也不刪除既有金幣列。`Shop/GetGold` 讀取餘額。新增 `AddAuthoritativeGoldAndShopRequests` migration 僅建立冪等交易紀錄並檢查既有金幣資料；套用前須核對及備份。
- `Economy/StartWave` 建立波次 session，`Economy/ReportKill` 驗證 session 與敵人索引並防重複領獎，`Economy/PurchaseSkill` 扣款解鎖技能。新增部署由 `GameProgress/Save` 每筆扣 60，需提供穩定 `request_id`；技能購買每次 100。金幣、波次 session 與技能解鎖參數不能經通用 CRUD 或進度快照繞過。
- 角色／經濟已完成前後端新契約串接，通過單元、實際 SQL Server 與正式前端 AppShell 的 HTTP 整合驗證；不相容舊本機抽卡流程。前端依後端角色清單判定擁有權，交易後對帳金幣，不再自行抽卡或修改遠端餘額。
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
