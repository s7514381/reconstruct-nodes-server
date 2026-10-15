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

## API 系統訊息契約

系統提示使用 `BackendApiResult<T>` 的 `Status`、`Success`、`MessageKey`、
`MessageArgs`、`Data`；已移除 `Message`，不提供舊中文訊息相容分支。
`MessageArgs` 是命名字串參數（例如金幣不足的 `cost`），沒有成功提示時
key 為空字串、args 為空物件。前後端必須同步部署。

後端以 `ApiMessageKeys` 與具型別業務例外選擇 key；前端負責翻譯及插值，
玩家名稱與資料不屬於系統訊息。交易失敗（包含非預期交易例外）維持 HTTP 400；
未處理例外回 500。未知錯誤只在伺服器記錄完整診斷，不對外傳例外文字。
模型驗證與無效 JSON 回 400 key 封包，授權失敗回 401 key 封包。
既有非提示型 CRUD 成功資料及舊登入的狀態資料維持原契約；
它們的交易失敗不再回空回應或成功形狀。

## 本機啟動

需要 .NET 10 SDK 與可連線的 SQL Server。先設定資料庫連線及下節的必要憑證：

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL Server connection string>" --project .\reconstruct-nodes-server
dotnet build .\reconstruct-nodes-server.sln
dotnet run --project .\reconstruct-nodes-server --launch-profile https
```

`https` profile 在預設 Debug 組態使用 `Development`，網址為 `https://localhost:7007`，與遊戲前端預設連線一致。
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

## 正式機組態

未明確指定環境時，Debug 建置預設使用 `Development`，Release 建置預設使用
`Production`。Visual Studio 的 http、https、IIS Express profile 不再固定環境，
切換為 Release 後即使用 Production；既有 profile 仍會開啟 `/swagger`，但
Production 不啟用 Swagger，因此該頁會回應 404。

組態先載入 `appsettings.json`，再以 `appsettings.{Environment}.json` 覆寫同名鍵；
環境檔未設定的鍵（包含同一區段內其他鍵）仍沿用基底值。
`appsettings.Production.json` 初始為 `{}`，不重複基底設定，只在有正式機差異時加入，
例如只調整連線上限：

```json
{
  "DatabaseConcurrency": {
    "MaxConnections": 20
  }
}
```

此例的 `MaxOperations` 仍繼承基底值。設定檔會隨建置與發佈複製到輸出，
不會合併或改寫 `appsettings.json`。

```powershell
dotnet run --project .\reconstruct-nodes-server --configuration Release
dotnet publish .\reconstruct-nodes-server --configuration Release
```

明確指定環境仍優先於建置組態預設，優先序為命令列 `--environment`、
`DOTNET_ENVIRONMENT`、`ASPNETCORE_ENVIRONMENT`。
例如 `dotnet run --project .\reconstruct-nodes-server -c Release -- --environment Staging`
會改用 Staging。

環境變數與命令列仍可覆寫 JSON 組態，例如 `ConnectionStrings__DefaultConnection`。
目前基底連線字串指向本機 SQL Server；空的 Production 檔也會繼承它，
**上線前務必由部署端提供正式機連線**。連線密碼、Google ClientSecret、SMTP 密碼等
敏感值應由部署平台的秘密設定／環境變數提供，不要寫入版本控制；
`user-secrets` 僅在 Development 自動載入。

Google 桌面登入可將 Google 下載的 Desktop OAuth JSON 原檔放入後端專案的
`Secret/google-desktop.json`；程式會讀取 `installed.client_id` 與 `installed.client_secret`。
整個 `Secret` 資料夾由 Git 忽略，但只有指定的憑證檔會隨建置與發布複製，
正式機可直接讀取。Android 登入的 Web OAuth JSON 放入 `Secret/google-web.json`，
程式自動讀取 `web.client_id` 作為 `ServerClientId`，此檔也會隨發布複製。
Android OAuth 原檔可保存為 `Secret/google-android.json`，但不載入、不發布。
CI 發布需另外提供此檔案。其優先序高於一般／環境 appsettings，
低於 Development User Secrets、環境變數與命令列。缺檔不改變原本行為。
發布產物包含明文 secret，務必限制存取；格式及操作見 [Google 登入設定](docs/GOOGLE_AUTH.md)。

Azure Storage 連線字串放在後端專案的 `Secret/azure-storage.json`：

```json
{
  "AzureStorage": {
    "ConnectionString": "<Azure Storage connection string>"
  }
}
```

此檔由 Git 忽略，但會隨建置與發布複製；CI 必須另外提供檔案，
發布產物包含明文金鑰，請限制存取。載入優先序高於一般／環境 appsettings，
低於 Development User Secrets、環境變數與命令列。
也可使用 `AzureStorage__ConnectionString` 覆寫，不必提供檔案；
若所有來源皆未設定，AzureStoreService 會明確回報設定錯誤。
已進入 Git 提交的金鑰必須另從提交歷史移除，並輪替真實金鑰。

完整的 `SmtpConfig` 區塊放在 `Secret/smtp.json`，appsettings 不保留 SMTP 設定：

```json
{
  "SmtpConfig": {
    "SenderName": "<sender display name>",
    "Server": "<SMTP server>",
    "Port": 587,
    "Account": "<SMTP account>",
    "Password": "<SMTP password>",
    "Sender": "<sender email>"
  }
}
```

此檔的 Git 忽略、建置／發布複製及載入優先序與 Azure 設定檔相同。
CI 需另外提供檔案，也可用 `SmtpConfig__Password` 覆寫。
發布產物包含明文密碼，請限制存取；已提交的密碼仍須另從 Git 歷史移除。

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

## 核心 SQL／HTTP fixture 整合驗證（須明確授權）

`CoreHttpIntegrationTests` 只允許本機 SQL Server，會建立一個隨機
`core-http-fixture-*` 訪客，以自己的獨立埠 backend 與記憶體 Azurite 執行真實 HTTP。
設定僅以子程序環境覆寫，不修改來源設定，不使用或停止共享服務，
也不將 HTTP 日誌寫入正式 Azure。須先安裝 Node.js／Azurite 並建置 Debug 後端。
**只有在已獲准使用該本機開發資料庫及精準刪除 fixture 資料後才可執行**；
不要對共享／正式資料庫執行，亦不要將下列 opt-in 設為一般測試的永久環境設定。

```powershell
# 從 server repo 根目錄執行；連線字串只留在目前程序環境，不輸出、不寫檔。
dotnet build .\reconstruct-nodes-server.sln --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$config = Get-Content .\reconstruct-nodes-server\appsettings.json -Encoding UTF8 -Raw | ConvertFrom-Json
$env:CHARACTER_SHOP_SQL_CONNECTION = $config.ConnectionStrings.DefaultConnection
$env:CORE_HTTP_SQL_TESTS = "1"
$env:CORE_HTTP_SERVER_DIRECTORY = (Resolve-Path .\reconstruct-nodes-server).Path
$env:CORE_HTTP_AZURITE_TABLE_SCRIPT = Join-Path $env:APPDATA "npm\node_modules\azurite\dist\src\table\main.js"
try {
    dotnet test .\reconstruct-nodes-server.Tests\reconstruct-nodes-server.Tests.csproj --no-build --no-restore `
      --filter "FullyQualifiedName~CoreHttpIntegrationTests" --logger "console;verbosity=detailed"
} finally {
    Remove-Item Env:\CORE_HTTP_SQL_TESTS,Env:\CORE_HTTP_SERVER_DIRECTORY,Env:\CORE_HTTP_AZURITE_TABLE_SCRIPT,Env:\CHARACTER_SHOP_SQL_CONNECTION -ErrorAction SilentlyContinue
}
```

測試涵蓋：新訪客唯一核心 kind=3、初始火球 kind=1／空手、重讀 GUID 不變；
只為該 fixture 透過 `WeaponService.Save` 的交易建立武器；
HTTP 保存武器與技能節點、保存空技能、相同 ClientId 重登保持空配置、
還原技能再重登保持 GUID／武器／技能。finally 先停止自有 PID，再以精確
Player GUID 刪除 fixture 子資料、以 GUID＋ClientId＋訪客條件刪除玩家；
驗證所有具 `CreateUserId` 欄位的實體表及該 ClientId 皆無殘留。
Azurite 關閉即丟棄記憶體日誌。若測試程序被強制終止，finally 無法保證執行；
須依測試輸出的 fixture ClientId／GUID 與自有 PID 做精準清理，不能廣泛刪玩家或停止同名程序。

已有一般角色而尚無核心的取得路徑，由既有 SQL rollback fixture 驗證；
使用已獲准的相同連線，在目前 shell 明確設定 `CHARACTER_SHOP_SQL_TESTS=1` 後，
以完整方法 filter 執行
`ReconstructNodesServer.Tests.Services.CharacterShopSqlTests.RealDatabase_DrawReplay_CharacterSkillsAndDeployment_PreserveGold`。
不要以整個 SQL category 取代此聚焦測試。2026-10-14 在明確授權的本機開發 DB
實際執行上述 SQL 與 HTTP 測試各 1 項，皆通過（包含 fixture 清理斷言）。

## API 概覽

- 固定角色與道具名稱由遊戲翻譯資源提供，後端不保存或回傳 `Character.Name`、`Item.Name`、角色 DTO 的 `name` 或抽卡的 `character_name`；商店 Catalog 不提供名稱。前端以 `kind`／`ItemKey`／商店識別碼取得顯示文字，不再支援角色改名。
- `RemoveStoredGameNames` migration 刪除上述兩個 DB 欄位，原有名稱無法由回滾恢復；部署須同步更新前端契約。`EconomyRequest.Result` 保留交易冪等紀錄，舊 JSON 即使含 `character_name` 也不再由新版抽卡 DTO 回傳；不清除其他交易資料。

- 裝置登入：`POST DeviceAuth/Login`。
- 管理端登入與 session：`POST Manager/Login`、`POST Manager/GetSession`（原 `GetManagerSession`，已改名，見 docs/ARCHITECTURE.md 10.2 的 URL 改名表）。
- 遊戲資料：`Shop`、`Character`、`GameProgress`、`Talent`、`Item`、`Weapon`、`AchievementProgress`。
- `Character/GetList`、`Character/Get?id=<GUID>`、`Character/Save` 提供獨立角色配置；角色未部署也能裝備武器與技能樹。部署只使用 `character_id` 引用角色目前配置，進度覆寫不刪除角色。
- 一般角色（kind 0～2）由 `Shop/Draw` 取得：單抽 100 金幣、十連 900，新角色配發普通基礎武器，重複發碎片。`Character/Save` 不能建立角色。抽卡需穩定 `request_id`，重送不重複扣款。
- 核心沿用 `Character` 與相同 GUID／配置 DTO，固定 **kind = 3**，不新增核心 endpoint 或資料表。新舊帳號首次 `Character/GetList`（或 `Get`）在既有交易與玩家鎖內自動補齊唯一核心；無費用、無初始武器，初始技能樹為 `[{ "kind": 1, "auxiliary_nodes": [] }]`。火球值已核對遊戲 `BulletData.FIREBALL -> ActiveSkill.FIREBALL = 1`，不是 4。已存在核心不重設技能或武器，明確保存的空技能清單會保留。
- 前端先讀 `Character/GetList` 取得核心真實 `id`，再用 `Character/Save` 保存相同 `id`、`kind: 3`、`skill_trees` 與可選 `weapon_entry_id`（空值表示卸下）；Save 回傳原 GUID。核心免費可用火球及既有通用基本技能，其他主動／輔助技能仍需解鎖；武器仍驗證本人擁有及跨角色唯一裝備。核心不進商店抽取池，不能放入 `GameProgress/Save.deployment_log`（沿用 `backend.progress.character_not_owned` 拒絕），沒有角色刪除／撤回 API。一般部署費不適用核心，核心常駐位置由前端接線；後端只保存核心配置。
- 本次核心擴充不需 migration；不會主動批次改寫帳號或共享資料庫，未讀角色清單的帳號於下一次讀取時補齊。服務層直接呼叫 `GetList`／`Get` 也必須沿用既有 repository transaction，不能脫離交易使用玩家鎖。
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
