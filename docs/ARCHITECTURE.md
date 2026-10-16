# 專案架構分析：`_ModelService` 與 `BaseServiceApiController` 的複用設計

本文件以目前倉庫程式碼說明分層架構、共用方法、擴充點（hook）與 API 行為。角色／經濟重構另外執行了單元、實際 SQL Server 與 HTTP 整合驗證；這不代表所有領域皆經過相同驗證。

> 主要程式位於 `reconstruct-nodes-server/`（ASP.NET Core，net10.0）與 `reconstruct-nodes-server.Library/`（net10.0，共用模型與工具）。
>
> 對應前端為獨立倉庫 **`reconstruct-nodes`**（Godot 專案，與本後端倉庫分開維護），透過 HTTP 呼叫本後端的 API。

---

## 1. 分層總覽

```
HTTP 請求
  └─ BaseController                 （共用基底）
      ├─ ManagerController          （不套用 ApiFilter；session 查詢套用 AuthorizeFilter）
      ├─ HomeController             （GET /，不套用 ApiFilter）
      └─ BaseApiController          （_logger、_lazy、TransactionFunc 交易包裝）
          └─ AuthorizeApiController （[ServiceFilter(AuthorizeFilter)] 驗證）
              ├─ BaseServiceApiController<DB, VM>   ← 一般實體的 CRUD 端點
              │    └─ AchievementController, PlayerController, PlayerParamsController
              └─ ShopController, CharacterController, GameProgressController, TalentController, ItemController,
                 WeaponController, AchievementProgressController ← 領域專用 API

  DeviceAuthController、LoginController 繼承 BaseApiController

Services
  BaseService                        （Dapper 封裝：QueryAsync / ExecuteAsync / InsertAsync / UpdateAsync / TransactionFunc / ExecuteCommandAsync / ReadBatchAsync）
    ├─ Commands/XxxCommand           （用例層：GoldCommand、EconomyCommand、RevisionCommand、BackgroundSyncCommand、ShopCommand、
    │                                  WeaponCommand、TalentCommand、GameProgressCommand、AchievementProgressCommand；只組合資料層 Service）
    └─ _RowService<DB>               （瘦基底：CRUD、軟刪除、Seed、自然鍵刪除、本人列／子列查詢與批次寫入 helper）
         ├─ 純子表 Service：AchievementCollectedService、WeaponAffixService、WeaponAffixValueService、WeaponUniqueValueService、
         │                  DeploymentService、BattlefieldRoadService、CharacterSkillTreeService、CharacterSkillNodeService、
         │                  EconomyRequestService、TalentAcquiredService、TalentLegacyCategoryService
         └─ _ModelService<DB, VM>    （ViewModel、列表、主從明細同步、Azure Table 轉移、Test 掛點）
              ├─ _SettingService<DB, VM>          （覆寫 SqlDataList，設定類實體的基底）
              │    └─ SystemConfigService
              ├─ _ModelSettingService<DB, VM, S, SM>   （目前無任何子類別，見第 7 節）
              ├─ 一般實體 Service：AchievementService, AchievementDetailService,
              │  ManyToManyService, PlayerService, PlayerParamsService, EmailVerifyService, ItemService
              └─ _SnapshotService<DB, Snapshot> （獨立快照 DTO 的 Save、Upsert 父列與限定欄位更新；子表讀寫改走子表 Service）
                   └─ CharacterService, TalentService, WeaponService

資料流向：`前端 → HTTP → Controller → Service → BaseService → IDapperRepository → DapperRepository → SQL Server`。Controller 處理 HTTP、部分輸入檢查與交易邊界；領域 SQL 與業務規則主要位於 Service。EF Core 的 `ApplicationDbContext` 負責模型與 Migration，Dapper 負責上述 Service 的資料操作。

### 領域 batch 與交易所有權

#### 統一背景入口與初始化邊界

`GameProgressController.BackgroundSync(BackgroundSyncRequest)` 為指定的單一背景入口，
回覆型別為 `BackendApiResult<BackgroundSyncResponse>`。
請求統整 kills、wave_starts、talent、achievement、items 與 weapon_acquisitions；
沿用現有強型別領域 DTO，不包含前景購買／領獎／精煉等操作。
回覆以 request_id 對應整批確認，並保留 gold、sessions、acquisitions 及受影響的權威資料；
可選欄位 null 不代表清空狀態。

`GameProgressController.Initialize()` 為聚合初始化入口，在交易內建立缺少的進度與天賦，不重設既有資料。
回覆 `BackendApiResult<PlayerInitializationResponse>`，包含角色、金幣、既有商店目錄、
進度、天賦、成就、物品、武器與 revision vector。必要欄位使用 required，
成就尚未建立時允許 null，物品／武器可為空清單；登入仍由既有認證入口負責。

`BackgroundSyncCommand.Execute` 使用 repository 的 `ExecuteAtomicJsonAsync` 擁有唯一外層交易，
Controller 指定 `commandOwnsTransaction: true`，不能再包交易。
`RevisionCommand.ExecuteIntent` 以既有交易與 ledger 保存固定意圖與回執（目前僅 Weapon/Acquire），不使用 Player 列鎖；
`Economy/PurchaseSkill`、`Weapon/Disassemble`、`Character/EquipWeapon` 與部署／戰場／切波命令同樣由
`commandOwnsTransaction: true` 的批次讀寫擁有交易，各為 2 次 SQL 往返；
重播不重複扣款／發獎，同 ID 異內容回覆 409，任一工作失敗全部回滾。
不要用外層 GoldCommand account 包整批，避免舊 account 覆蓋內部最新金幣。
背景不再儲存客戶端 progress；成就（擊殺、各難度通關波數、武器與詞綴收集）由伺服器依自身資料推導，
`AchievementProgress/Get` 與 Initialize 即時組出快照，BackgroundSync 與領取請求中的成就快照一律忽略，
領取時才把推導結果落地並標記領取；kills 依 session 規則累加擊殺、經驗及升級天賦點，
只寫進度參數而不重建部署／道路。天賦花費與節點、成就領取紀錄仍由原領域合併保護。
讀取只涵蓋該批次用得到的領域：武器取得不讀取既有武器（查重由寫入批次內的 UPDLOCK 檢查負責，
回應只帶 `acquisitions[]`，`weapons` 為 null）；天賦列只在請求帶 `talent` 或擊殺造成升級時才讀，
升級時以 `TalentReadRequired` 回滾後帶天賦讀取重跑一次（一般擊殺批次不付這個成本）。
擊殺不再每殺寫一列 `enemy_kill` EconomyRequest：已領取的敵人索引存在單一 PlayerParams 列
`economy/claimed_kills`（`KillLedger`，格式 `{sessionId}:{base64 位元圖};…`，只保留最近 4 個波次）。
同敵人再次回報（任何 request_id）視為已套用，不發獎也不計數；kill 的 `session_id` 必須是進行中波次
或位元圖內的最近波次，否則回 `backend.economy.session_not_owned`。批次回執也不再每批新增一列
`background.sync`：最近 2 批的回執（request_id、意圖雜湊、結果）存在單一 PlayerParams 列
`economy/sync_receipts`（`SyncReceiptRing`），重送同 request_id 回原結果、不同意圖回 409；
回執不存庫存快照（避免隨庫存變大）；重放時在同一段 SQL 補上「目前」在庫物品，不會重複套用增量。
客戶端同時只有一批在途，未知結果重試的一定在這 2 批內。舊版 `background.sync` 列仍可重播但不再新增。
`wave_start` session 回執仍保留。`EconomyRequestRetentionService` 每 24 小時（啟動後 10 分鐘首次）分批刪除超過
`EconomyRequestRetention:Days` 的 EconomyRequest，並保留進行中波次的 `wave_start` 列。
預設 0（停用）：刪除 SQL 尚未對真實資料庫驗證，確認後再以設定（例如 30）啟用。
LatencyBaseline 的 fixture 後端固定設為 0，不會對共用資料庫執行清理。`WaveComplete` 以位元圖（並相容舊版 `enemy_kill` 收據）驗證全部敵人已回報；
舊收據不再新增，確認無舊資料後可移除相容讀取。
Godot 有異動每秒最多派一批、最多一批在途；派送前保存帳號隔離 journal，
未知結果重試原批次，新事件留至下一批；開機先恢復未決批次。
自動開波亦走批次並等待 session，前景 action 不等待無關背景排空。
`GameProgress/WaveComplete` 為自動完成命令，只接受 request_id 與 session_id。
確認本人 active session 全部敵人已回報擊殺後，保存通關紀錄、最大波數及完成狀態，
回傳進度與下一波目標；重送使用同一永久回執。前端確認後才進入波間等待，
下一波仍經 wave_starts 建立 session。Initialize 與 WaveComplete 均由 Controller 包交易。
progress 專用 flush 已移除；skill_suffixes、refine_item_counts 不再輸出／讀寫，歷史 DB 列保留。

#### 既有領域執行路徑

快照清單讀取的 HTTP 入口呼叫 Service 的 `Get*Command`，
由單次參數化 SQL batch 管理 Snapshot 一致性讀取與提交，不取得玩家列鎖；Controller 使用 `CommandFunc`
或 `TransactionResultAsync(commandOwnsTransaction: true)`，不能再外包一個 client transaction。
`_SnapshotService` 的讀取 helper 使用現有 `QueryMultipleAsync`，完整消費提交確認結果後才回傳；
寫入統一由組合交易內 `Save`／revision 檢查與推進承接，舊獨立 SaveCommand 已移除。
衝突、業務拒絕及非預期錯誤仍使用原 HTTP／本地化訊息邊界，不以預設資料代替失敗。

同一領域原有 `Get`／`Save` 方法仍供已持有外層交易的組合用例使用
（例如天賦命令、角色穿裝與武器操作），模型映射及可重用寫入 batch 共用；
不能在這些組合呼叫中另行提交。物品 `AdjustAmount` 只使用呼叫端交易，
保留分類補入、負數拒絕、溢位與歸零軟刪除；舊頂層 AdjustAmountCommand 已移除。
波次／抽取透過 `GoldCommand.ExecuteBatch` 共用回執與金幣資源原子寫入，
回執重播先於業務檢查；已有 repository transaction 時由原外層負責提交。
抽取仍依結果順序判斷持有、新角色與碎片，碎片增量重用共用物品 SQL；
回應保留逐抽結果與最終持有量，不把機率或獎勵判定改為背景作業。

### `_ModelService` 泛型命令試點：ReportKill

`BaseService.ExecuteCommandAsync<TContext, TResult>(read, execute)` 管理交易生命週期：
讀取與後續操作共同成功才提交，例外回滾並原樣傳遞；已有 repository transaction 時不另開、
提交或回滾外層交易。它不含玩家、金幣、回執、SQL 變數或特定 DTO 的假設。
`ReadBatchAsync<TResult>(sql, parameters, read)` 管理多結果 reader 與映射，
先完整物化必要資料並釋放 reader，再進行後續寫入，避免持有 repository gate 時重入。

`ReportKill` 與 `BackgroundSync` 使用泛型命令骨架；`GoldCommand` 組合基底的泛型命令與批次讀取，
負責玩家互斥、回執重播與金幣／回執共同保存；`EconomyCommand` 處理 session／重複擊殺驗證，
`KillRewardRules.Calculate` 為不存取 DB 的 C# 獎勵公式。規則不再同時存在 C# 與擊殺 SQL 兩份。
共用持久化 SQL 與既有 batch 路徑使用同一份，不為試點複製金幣／回執寫入。

此形狀也可套用非經濟模型，不需要重寫交易與 reader 清理；跨 Weapon／Talent 模型、
不同結果型別的測試驗證同一基底方法，但尚未遷移這些正式 API。
代價是 C# 計算位於讀取與保存之間，client transaction 的開始、讀取、保存與提交仍需多次往返。
因此此試點是可維護性的比較版本，不能宣稱比原單次 SQL batch 快或已達 100 ms。

獨立 I/O 可使用 `List<Task>`／`Task.WhenAll`，但同一 repository 的連線與交易仍序列化，
不為並行移除 gate 或拆開原子操作。交易內往返優先以 batch／多結果查詢縮減；
非同步 I/O 不須額外 `Task.Run`。量測與隔離實作見 [延遲診斷](LATENCY-DIAGNOSTICS.md)。
單次 batch 不代表固定低於 100 ms；驗收須分別檢查每端點的後端 P95、
功能回歸與 fixture 清理，並觀察 SQL CPU／IO。不可僅以平均值或往返次數宣稱達標。

`Program.cs` 將 `IDapperRepository` 與公開、非抽象的 Argument／Service／Filter 類別註冊為 scoped；`Lazy<T>` 為 transient，`MailSender`、`AzureStoreService`、`FirebaseService` 為 singleton。DB limiter 明確以 singleton 註冊並排除反射 scoped 掃描。`TimedHostedService` 的註冊目前被註解，啟動流程不會自動執行遷移。

`DbConnectionLimitService` 與 `DbOperateLimitService` 共用移植的 `SemaphoreSlimService`
lease 機制，分別控制同一程序的 Dapper 連線數與 DB 操作數；設定位於
`DatabaseConcurrency`，兩個上限預設均為 10，必須為正整數，不做動態調整。
Repository 建構時不開啟連線，非交易操作結束即關閉，交易持有到 Dispose。
同步、非同步及 Contrib 方法均由 Repository 的共用執行 helper 控制，
同一 Repository 的命令序列化；取得順序為本地互斥、連線額度、操作額度，
避免等待連線者佔滿操作額度而阻塞既有交易。
交易命令、開始、提交、回滾及釋放均受操作限制，但不鎖住整段交易委派。
多結果查詢改回傳 `ManagedGridReader`，持有額度到 reader Dispose，須使用
`using`／`await using`，不可在 reader 尚未釋放時對同一 Repository 發出命令。
EF Core Migration、SqlClient 連線池與非 DB 的 semaphore 不受此機制影響。

Dapper 連線字串未指定 `Min Pool Size` 時預設為 5，且啟動時先開啟一次連線讓連線池補滿；Azure SQL 的實體連線建立約 0.4 秒以上，常駐連線避免並行請求與閒置後的第一個請求各自付出這個成本。

---

## 2. `_ModelService<DBModel, ViewModel>` 可複用方法

泛型參數約束：`DBModel : ModelBase, new()`、`ViewModel : DBModel, new()`。`ViewModel` 是 `DBModel` 的衍生型別，用來承載主檔加上子明細集合（例如 `AchievementModel.DetailList`）。

### 2.1 查詢

| 方法 | 可見性 | 說明 |
|---|---|---|
| `SqlData()` | `public virtual` | `SELECT * FROM {table} WHERE {key} = @Id`；本身不加擁有權或 Status 條件 |
| `GetData(Guid)` | `public virtual` | 取得 DB 原始模型 |
| `GetModel(Guid)` | `public virtual` | 取得 ViewModel，並呼叫 `SetModel` hook 載入子資料 |
| `Sql(select, from, join, where, subWhere, order)` | `public virtual` | 組裝 SQL 的共用建構器，預設 `main.[Status] != -1`（軟刪除過濾） |
| `SqlDataList(subWhere)` | `public virtual` | 在 `Sql` 之上加上 `CreateUserId = @CreateUserId`（多租戶隔離） |
| `GetDataList()` | `public virtual` | 目前玩家的 DB 模型清單 |
| `GetModelList()` / `GetModelList(IEnumerable<Guid>)` | `public virtual` | 目前玩家的 ViewModel 清單（含子資料） |
| `SqlMTMDataList` / `GetMTMDataList` / `GetMTMModelList(sourceId)` | `public virtual` | 透過 `ManyToMany` 關聯表查詢 |
| `SetModelList(list)` | `public virtual` | 並行呼叫每筆的 `SetModel` |
| `GetModelListByParent<TParent>(parentId)` | `public virtual` | 以外鍵（由 `CommonUtility.GetForeignKeyField` 推導）取子清單，依 `Sequence` 排序 |
| `IsExist(Guid)` | `public virtual` | 以 Id 計數，使用 `NOLOCK`；不過濾擁有權或 Status，與 Save 的存在性查詢不同 |

### 2.2 寫入

| 方法 | 可見性 | 說明 |
|---|---|---|
| `Save(ViewModel)` | `public virtual` | 單筆 upsert，委派給 `Save(IEnumerable)` |
| `Save(IEnumerable<ViewModel>)` | `public virtual` | 批次 upsert：先查本人既有 Id（僅限本人資料，見第 7.5 節），再分別呼叫 `UpdateModel` / `InsertModel`（會觸發 `ModelSave` hook） |
| `SaveData(DBModel)` / `SaveData(IEnumerable<DBModel>)` | `public virtual` | 與 `Save` 相同的 upsert 流程，但直接用 `UpdateAsync` / `InsertAsync`，**不觸發 hook** |
| `UpdateModel` / `InsertModel` | `protected virtual` | 寫入 `CreateUserId = _playerId`，呼叫 `ModelSave` 後寫入 |
| `UpdateDetail<TParent>(parent, list)` | `public virtual` | 主從明細同步（新增、更新、刪除差集；刪除僅限本人資料），外鍵由 `CommonUtility.GetForeignKeyField` 推導 |
| `DeleteModel(ViewModel)` / `DeleteModel(IEnumerable<ViewModel>)` | `public virtual` | 預設先以 `Status = -1` 軟刪除本人列，略過非本人 Id。會觸發 `ModelDelete`；另啟背景工作將請求模型轉存 Azure Table，轉存成功後硬刪除 SQL 列，未等待此背景工作完成 |
| `DeleteByNaturalKey(string naturalKey)` | `public virtual` | 依自然鍵軟刪除本人資料，回傳受影響筆數。子類別需要前置檢查時 override 並呼叫 base（例：`WeaponService` 先鎖定玩家並檢查是否仍被裝備） |
| `DeleteOwnedRowsAsync(tableName, parentColumn?, parentId?, ownedParentTable?, ancestorColumn?)` | `protected` | 共用硬刪除機制；可依父鍵限定，或以本人父列子查詢刪除孫列（內外層均檢查擁有權）。快照服務透過強型別包裝呼叫，不傳入外部 SQL 識別字 |
| `LockPlayerAsync()` | `protected` | 交易內以 `UPDLOCK, HOLDLOCK` 鎖定目前玩家列，供進度寫入與武器刪除共用 |
| `SqlDelete(subWhere)` | `public virtual` | 產生刪除用 SQL |

### 2.3 擴充點（Template Method hook）

獨立快照 DTO 不需要繼承 DBModel：`_SnapshotService<DBModel, Snapshot>` 沿用
`_ModelService<DBModel, DBModel>`，提供 `Save(Snapshot)`，先呼叫 `ValidateSnapshot`，
再呼叫 `SaveSnapshot`。Character、Weapon、Talent、AchievementProgress 的公開 Save
委派基底執行此流程；GameProgress 的 override 先驗證，再於 `SaveSnapshot` 內依交易回執
處理 revision 與部署扣款。客製化驗證及寫入位於對應 hooks。
Weapon 的詞綴與特殊屬性仍在 `SaveSnapshot` 開始時完整解析，早於任何資料庫操作。

`UpsertSnapshotParentAsync` 共用本人父列的 `UPDLOCK, HOLDLOCK` 查詢及新增／更新分支。
Weapon 使用 EntryKey 定位，透過 `UpdateSnapshotFieldsAsync` 僅更新 WeaponKey、WeaponFamilyKey、
Rarity、UniqueKey，WHERE 保留 Id 與目前玩家；不更新 EntryKey、Status 或建立資訊。
Talent 以玩家定位、Character 以 Kind 定位，仍使用完整 UpdateAsync。
GameProgress 與 AchievementProgress 保留各自的整份覆寫策略，不使用父表 upsert。
兩者及 Character 的技能子列替換先建立具型別列（含 GUID、建立時間及各集合獨立 Sequence），再以 JSON 參數和
`OPENJSON ... WITH` 在單一 SQL command 中刪除及集合式插入；不再每列往返資料庫。
SQL 的寫入擁有者固定取目前玩家，不信任 JSON 的 CreateUserId，所有刪除同樣限制本人。
成就仍先鎖群組並讀取已領獎狀態，重建時只保留伺服器的領獎紀錄；
進度仍保留金幣與伺服器解鎖欄位，部署驗證、扣款及冪等回執的先後順序不變。
空集合仍執行對應清除，後段插入失敗由原交易回滾，不以提早回覆或延後保存降低時間。
所有快照儲存仍需由 Controller 的交易包裝呼叫，不在 Service 新增交易。

快照基類只集中持久化機制：表名與關聯鍵沿用 `CommonUtility` 的 Table／Relation metadata，
排序與更新欄位使用直接屬性 expression；缺少 metadata 或無效欄位選取會明確失敗。
衍生類別保留 DTO 轉換、領域驗證、父子依賴順序與鎖定編排，不以設定驅動通用 SQL 框架。

| 快照方法（皆為 `protected`） | 責任與使用端 |
|---|---|
| `UpsertSnapshotParentAsync(map, naturalKey?, update?)` | 鎖定本人有效父列後新增／更新；Weapon、Talent、Character。新父列不刪明細，既有列先更新再由領域流程替換明細 |
| `UpdateSnapshotFieldsAsync(row, fields)` | 強型別限定更新；拒絕 Id、CreateUserId、CreateDate、Status 與自然鍵，Weapon 使用 |
| `QuerySnapshotRowsAsync<T>(orderBy?, lockRows?, ids?)` | 本人有效列查詢，可批次限定 Id；Weapon 依 CreateDate、Character 依 Kind、成就群組與進度道路／部署依 Sequence 排序。成就群組鎖仍為 UPDLOCK、HOLDLOCK |
| `QuerySnapshotParentAsync<T>()` | 本人有效父列 TOP 1；Talent 無資料時仍回傳 null |
| `QuerySnapshotChildrenAsync<TChild, TParent>(parentIds?, activeChildrenOnly?, activeParentsOnly?)` | 批次父子關聯、雙層擁有權、Sequence 排序；Weapon、Talent、Character、AchievementProgress 共用，不新增逐父查詢 |
| `DeleteSnapshotRowsAsync<T>()` / `DeleteSnapshotChildrenAsync<TChild, TParent>(parentId)` | 型別安全的本人列／子列硬刪除，委派既有 DeleteOwnedRowsAsync |
| `DeleteSnapshotDescendantsAsync<TChild, TParent, TAncestor>(ancestorId)` | Weapon 詞綴值與 Character 技能節點共用；先刪孫列再刪子列，內外層皆檢查 CreateUserId，清理時不排除停用列 |
| `InsertSnapshotRowAsync<TRow>(row)` | 統一 CreateUserId，不變更 Sequence；核心初始化與父表 upsert 等單列寫入使用 |
| `InsertSnapshotRowsAsync<TInput, TRow>(inputs, map, afterInsert?)` | 每個集合序號從零開始，設定本人擁有權；Weapon、Talent 依序插入後才回呼寫入下一層。Character 技能子列、GameProgress、AchievementProgress 改用各領域集合式 SQL，進度純量仍保留 null 序號 |

Character/GetList（Get 也委派 GetList）是核心取得入口：在 controller 交易內先取得
Player 的 UPDLOCK/HOLDLOCK，再檢查本人有效 Character 清單；缺少 kind=3 才新增
Character 與一棵 kind=1 火球技能樹，不發武器、不碰金幣。既有核心（包含空技能）
完全不補預設。沿用既有玩家／Kind 唯一索引、GUID、技能子表及 Save 所有權驗證，
不新增 schema 或獨立核心資料來源。GetList 因此是可能寫入的讀取，直接服務呼叫也須包交易。
Save 對所有合法種類 0～3（即使空技能）先檢查種類，核心將火球視為免費預設技能；
一般角色與技能解鎖規則不變。GameProgress 的角色引用查詢僅接受 kind 0～2，
所以核心不能混入一般部署；拒絕發生在扣款、刪除或重寫進度之前。
Shop Catalog 維持一般三職業，CharacterController 只公開 GetList/Get/Save，不提供角色移除。

核心聚焦驗證：`CoreCharacterTests` 使用記憶體 repository／交易鎖替身驗證新舊帳號、
並行首次讀取的單一 GUID、初始火球空手、既有空配置、技能解鎖及部署 SQL guard。
`SnapshotSaveTests` 另覆蓋核心裝備所有權、跨角色重複裝備及空配置儲存；
`CharacterShopSqlTests` 的 opt-in SQL 測試已補核心取得／空配置／禁止部署斷言，
最初的離線驗證未執行 SQL；鎖替身不等同真實 SQL 隔離與回滾驗證。
後續於 2026-10-14 獲明確授權後，已使用設定的本機開發 DB 執行該聚焦 SQL rollback
測試，1/1 通過。新增 opt-in `CoreHttpIntegrationTests`，透過單一隨機訪客及自有
HTTP backend／記憶體 Azurite 驗證核心初始配置、GUID、武器與技能保存、空技能與還原配置
跨兩次真實重新登入皆保留，當時 1/1 通過。本次 runner 已改成 fixture 玩家下經
Weapon/Acquire（含重播）與 Character/EquipWeapon 建立／穿裝，**尚未重新執行真 HTTP fixture**；
finally 停止自有程序、精準刪除該玩家與子資料，再查驗所有 CreateUserId
實體表與 fixture ClientId 無殘留。未執行 migration、未使用既有帳號或停止共享服務。
重跑命令、環境前提及強制終止限制見 README 的核心 SQL／HTTP fixture 整合驗證。

讀取狀態語意：父列皆排除 Status = -1；Character 的技能樹與節點也排除停用列，
Weapon／Talent／成就的明細則不額外過濾自身狀態。Weapon 詞綴值的父詞綴也不過濾狀態，
但仍需屬於本人已讀取的詞綴。空集合與 null 快照語意不變。
成就仍先鎖群組再讀已領取紀錄，保留伺服器 RewardDate／IsReward；
GameProgress 仍先鎖玩家、驗證角色，再由 GoldCommand 的 RequestId 冪等流程扣款與替換，
不刪寫 gold、伺服器掌管的子彈／輔助技能解鎖群組。
`SnapshotSaveTests` 與 `SnapshotPersistenceTests` 使用 recording repository 驗證 SQL／參數、
順序與轉換；真實 SQL Server 的鎖、隔離與回滾另需隔離資料庫整合測試，不能以 mock 結果代替。

| Hook | 可見性 | 預設 | 典型用途 |
|---|---|---|---|
| `SetModel(ViewModel)` | `protected virtual` | 空 | 讀取後載入子資料，例：`AchievementService` 載入 `DetailList` |
| `ModelSave(ViewModel)` | `protected virtual` | 空 | 主列寫入前處理子明細，例：`AchievementService` 呼叫 `UpdateDetail` |
| `ModelDelete(ViewModel)` | `protected virtual` | 空 | 刪除時的連動處理；與刪除 SQL 排入同批工作，沒有保證先後順序 |
| `NaturalKeyField` | `protected virtual` | `null` | 自然鍵欄位名稱（`WeaponService`：`EntryKey`；`ItemService`：`ItemKey`），供 `DeleteByNaturalKey` 使用；未設定時拋出 `InvalidOperationException` |
| `SqlDataList(subWhere)` | `public virtual` | 加上 `CreateUserId` | 設定類實體改寫過濾條件（`_SettingService`） |
| `Seed()` | `public virtual` | 空 | 種子資料；兩個 Controller 基底的 Seed 端點皆僅在 DEBUG 編譯 |
| `TestSave()` / `TestSaveList()` / `TestDelete()` | `public virtual` | 讀取並儲存／刪除既有資料 | DEBUG 端點呼叫，會操作真實資料，不是自動化測試 |
| `AzureTableTransfer(...)` | `public` | — | 將資料轉移到 Azure Table，接受委派 `Func<_ModelService, TableEntity, Task>` |

---

## 3. `BaseServiceApiController<DBModel, ViewModel>` 端點

建構子：`BaseServiceApiController(BaseApiArgument argument, _ModelService<DBModel, ViewModel> service)`。此基底的端點皆為 `[HttpPost]`；除 `Seed` 外皆為 `virtual`。

| 端點方法 | 呼叫的 Service 方法 | 回傳 |
|---|---|---|
| `GetData(RequestModel)` | `GetData(Guid)` | `DBModel?`（Id 缺失回傳 `null`） |
| `GetModel(RequestModel)` | `GetModel(Guid)` | `ViewModel?`（Id 缺失回傳 `null`） |
| `IsExist(RequestModel)` | `IsExist(Guid)` | `bool?` |
| `Save(ViewModel)` | `Save(ViewModel)` | `200` / `400` |
| `SaveList(List<ViewModel>)` | `Save(IEnumerable)` | `200` / `400` |
| `SaveData(DBModel)` | `SaveData(DBModel)` | `200` / `400` |
| `SaveDataList(List<DBModel>)` | `SaveData(IEnumerable)` | `200` / `400` |
| `GetDataList()` | `GetDataList()` | `IEnumerable<DBModel>?` |
| `GetModelList()` | `GetModelList()` | `IEnumerable<ViewModel>?` |
| `Delete(ViewModel)` | `DeleteModel(ViewModel)` | `200` / `400` |
| `DeleteList(List<ViewModel>)` | `DeleteModel(IEnumerable)` | 空清單 `400`，否則 `200` / `400` |
| `TestSave` / `TestSaveList` / `TestDelete` | 對應 `Test*` | **僅 `#if DEBUG`** |
| `Seed()` | `Seed()` | **僅 `#if DEBUG`** |

交易處理：寫入型端點皆透過 `BaseApiController.TransactionFunc` 包裝，成功回 `Ok()`，失敗透過 `TransactionFailure` 回 HTTP 400 key／args 封包。空刪除清單回 `backend.request.invalid`。

例外是 DEBUG 的 `Seed`：直接呼叫 Service，不使用上述交易包裝。
`BaseApiController.DeleteByKeyAsync` 共用空鍵檢查、交易與 `BackendApiResult<int>` 回應，
供 `ItemController.Delete` 使用；武器不再暴露通用 Delete，改由批次分解 action 驗證並回執。

### 3.1 子類別覆寫情況

- `PlayerController` 覆寫 `GetModel`，改用 `service._playerId` 作為 Id 來源，而不使用 `request.Id`（[PlayerController.cs](../reconstruct-nodes-server/Controllers/PlayerController.cs) 第 18 行附近）。這是把「Id 來源」寫死在子類別，導致流程重複（見第 6 節）。
- `AchievementController` 在自身程式碼中手動呼叫 `TransactionFunc`（第 27 行附近），未使用 `BaseServiceApiController` 已有的流程。

---

## 4. 新增一般實體的標準步驟

在已有資料模型與資料表的前提下，標準 CRUD 的 Service／Controller 可用以下兩個類別提供，不需手寫基本端點或 SQL：

```csharp
// Services/Xxx/XxxService.cs
public class XxxService(BaseServiceArgument argument) : _ModelService<Xxx, XxxModel>(argument)
{
    // 只在需要時覆寫 hook，例如：
    protected override async Task SetModel(XxxModel viewModel)
    {
        await base.SetModel(viewModel);
        // 載入子資料
    }
}

// Controllers/XxxController.cs
public class XxxController(BaseApiArgument argument, XxxService service)
    : BaseServiceApiController<Xxx, XxxModel>(argument, service)
{
}
```

Step 1：在 `reconstruct-nodes-server.Library/Models/DBModel` 定義 `Xxx : ModelBase`（含 Dapper `[Table]`；主鍵沿用 `ModelBase` 的 `[ExplicitKey]`，供共用工具讀取）。
Step 2：在 `reconstruct-nodes-server.Library/GameModel` 定義 `XxxModel : Xxx`（ViewModel）。
Step 3：建立上面兩個類別。
Step 4：依需要覆寫 `SetModel`、`ModelSave`、`ModelDelete`，或在 Controller 覆寫端點。

這兩個類別僅提供標準 CRUD 流程，不代表資料表已存在。新資料表還需加入
`ApplicationDbContext` 並建立／審閱 Migration；公開、非抽象 Service 須位於
`ReconstructNodesServer.Services` 命名空間下，並按現有模式提供建構子相依性。
需要 `_lazy` 存取時也要更新 `LazyServiceArgument`。

---

## 5. SOLID 與物件導向評估

### 5.1 做得好的部分

- **Template Method**：`Save` / `DeleteModel` / `GetModel` 固定流程，差異由 `SetModel` / `ModelSave` / `ModelDelete` 注入。這是最主要的複用機制，應保留並推廣。
- **泛型基底**：`_ModelService<DB, VM>` 與 `BaseServiceApiController<DB, VM>` 以型別參數取代手寫 CRUD，新增實體不需重複撰寫樣板程式碼。
- **明確的繼承階層**：Controller 與 Service 各自有獨立的基底鏈，職責邊界清楚。
- **型別約束**：`where DBModel : ModelBase, new()` 與 `where ViewModel : DBModel, new()` 在編譯期保證 ViewModel 是 DBModel 的子型別。

### 5.2 SRP（單一職責）

- `_ModelService` 同時負責 SQL 組裝、CRUD、主從明細同步、Azure Table 轉移與測試/種子資料。其中 **Azure 轉移** 與 **明細同步** 屬於獨立關注點，可考慮抽成協作者類別，但目前不建議為此增加抽象層。
- `BaseService` 提供資料存取便利方法；連線與操作的固定併發上限集中於 Repository 和 singleton limiter，已移除靜態 semaphore 自動調校。

### 5.3 OCP（開放封閉）

- 新增實體不需修改基底類別，符合 OCP。
- 但 `PlayerController.GetModel` 覆寫是為了改變 Id 來源，這代表基底對「Id 從哪來」沒有擴充點，只能整段覆寫。建議在基底新增 `protected virtual Guid? ResolveRequestId(RequestModel)` 之類的 hook。

### 5.4 DIP（依賴反轉）

- 大量程式碼透過 `_lazy`（`LazyServiceArgument`）以 `_lazy.XxxService.Value` 取得具體型別，等同於 Service Locator，依賴的是具體類別而非抽象。短期內改動成本高，本次分析不建議擴大抽象。
- `BaseServiceApiController` 只依賴 `_ModelService<DB, VM>`（抽象基底），此部分符合 DIP。

### 5.5 LSP（里氏替換）

- `AchievementService` 目前只透過 `SetModel`／`ModelSave` 等方法擴充，不再宣告 `SaveData(AchievementModel)` 多載。
- `SystemConfigService.Seed()` 使用 `override`，不隱藏基底方法。

### 5.6 ISP（介面隔離）

- 專案未定義介面，`_ModelService` 是單一大型抽象類別，承擔所有 CRUD、明細、測試與種子功能。子類別被迫繼承全部成員（即使不需要 `Test*` 或 `AzureTableTransfer`）。
- 若日後需要拆分，可依用途區分為「讀取」、「寫入」、「測試/種子」三組介面，但目前不建議為此導入介面體系。

### 5.7 封裝與強型別

- `_tableName`、`_keyField` 為 `public` 可變欄位；`_playerId`、`_player` 則是 public 唯讀運算屬性，不能直接指派。欄位可考慮縮小可見性或改為 readonly，需先確認外部使用端。
- `Save` 與 `SaveData` 的成對方法依靠 `DBModel` / `ViewModel` 的靜態型別分流，這是一個合理的強型別設計，但也造成第 6 節所述的重複。

---

## 6. 重複與待改善項目（依影響排序）

| # | 項目 | 位置 | 影響 |
|---|---|---|---|
| 1 | 通用 CRUD 端點仍重複交易與 Ok 樣板，失敗已共用 key／args 回應 | `BaseServiceApiController.cs`、領域 Controller | 保留成功資料契約；失敗使用 `TransactionFailure`，刪除自然鍵使用 `DeleteByKeyAsync` |
| 2 | `GetData`、`GetModel`、`IsExist` 各自重複 `if (!request.Id.HasValue) return null;` | `BaseServiceApiController.cs` | 中：可抽成泛型 helper 或 `ResolveRequestId` hook |
| 3 | `PlayerController.GetModel` 整段覆寫只為改 Id 來源 | `PlayerController.cs` | 中：應改為 hook，子類別只覆寫 Id 來源 |
| 4 | `Save(IEnumerable)` 與 `SaveData(IEnumerable)` 的存在性查詢與分支邏輯完全相同，只差在 `UpdateModel`/`InsertModel` 與 `UpdateAsync`/`InsertAsync` | `ModelService.cs`（約第 172、199 行） | 中：可抽出 `private` 共用流程，以委派傳入差異 |
| 5 | 存在性查詢使用 `Sql()` 預設的 `SELECT main.*`，只需要 Id 卻取回整列 | `ModelService.cs` | 中：改為只選取 `_keyField`，需實機驗證 Dapper 行為 |
| 6 | 外鍵與主鍵解析集中在 `CommonUtility`；`BaseFunction` 與 `ModelSettingService` 使用共用工具 | `CommonUtility.cs`、`BaseFunction.cs` | 現況：不需再提出重複實作的修正建議 |
| 7 | `CommonUtility.GetTableAttribute<T>` 以迴圈遍歷屬性，但 `prop.ReflectedType` 永遠等於 `T`，迴圈無意義 | `CommonUtility.cs` | 低：可改為 `typeof(T).GetCustomAttribute<TableAttribute>()` 並回退到基底型別 |
| 8 | `DeleteList` 檢查 `Count == 0`，但 `Delete` 不檢查，行為不一致 | `BaseServiceApiController.cs` | 低 |
| 9 | `GetModelListByParent` 宣告未使用的 `List<Task> tasks` | `ModelService.cs`（約第 120 行起） | 低：死碼 |
| 10 | `_ModelSettingService<DB, VM, S, SM>` 無任何子類別，其 `GetModelList` 覆寫也未被使用 | `Services/Base/ModelSettingService.cs` | 低：疑似死碼，需確認後再移除 |
| 11 | `_tableName`、`_keyField` 為 public 可變欄位；玩家資訊為 public 唯讀屬性 | `ModelService.cs` | 低：可評估縮小可見性 |

---

## 7. 已知風險與行為不一致

### 7.1 `Seed` 端點的 DEBUG 守衛

兩個 Controller 基底的 `Seed()` 皆位於 `#if DEBUG`，Release 不編譯這些端點。
這是組建條件，不是 `Development` 環境條件；DEBUG 的 Seed／Test 會操作實際資料。

### 7.2 `SaveData` 的 hook 略過

`SaveData` 系列不會呼叫 `ModelSave`，`Save` 系列會。兩者在呼叫端的選擇會影響是否觸發子明細同步。`BaseServiceApiController` 同時提供兩者，呼叫者需要自行判斷，這是潛在的誤用來源。

### 7.3 軟刪除與主鍵衝突（待執行驗證）

`Save`／`SaveData` 的 `GetExistIdSet` 使用 `Sql()`，預設過濾 `Status != -1`。本人已軟刪除的列因此不列入更新集合，再存相同 Id 會走新增路徑，可能觸發主鍵衝突。這不適用於不過濾 Status 的 `IsExist(Guid)`。

以上是依查詢與分支推導；本次未執行資料庫測試，不保留無倉庫測試佐證的歷史「已驗證」結論。

### 7.4 `SystemConfigService.Seed()` 隱藏基底方法（已修正）

已修正：現已使用 `override`，此問題不再存在。

### 7.5 存檔與查詢的擁有權處理

`Save` / `SaveData` 的存在性查詢（`GetExistIdSet`）已限定為目前玩家（`CreateUserId`），避免以他人的 Id 覆寫資料。查無此列時會改走新增路徑，結果依實體而不同：

- **角色**：他人的 Id 不列入更新集合，會走 `InsertModel`；若資料庫已有相同主鍵，預期交易回滾並回傳 400，仍待執行驗證。
- **武器**：低階持久化以 `(CreateUserId, EntryKey)` 定位；API 的 `Id` 是 EntryKey，不是資料庫 GUID。公開寫入只透過 `WeaponCommand` 的取得／精煉／批次分解，取得拒絕任何既有 EntryKey（含 tombstone），不提供通用 upsert API。

武器與道具以自然鍵刪除本人資料，查無本人列通常回 `Data = 0`；武器仍會先檢查本人的裝備參照。通用 `DeleteModel` 則略過非本人 Id。

擁有權過濾並非所有查詢都有：通用 `GetData`／`GetModel`／`IsExist` 只依 Id 查詢，
`GetModelListByParent` 與 `SqlMTMDataList` 基底也不加目前玩家條件。
不能將清單與領域 API 的本人資料過濾推廣為所有端點的保證。

---

## 8. 編碼與其他觀察

- 部分原始檔使用 UTF-8 BOM；遇到疑似亂碼需檢查實際 bytes 與解碼，不可只依終端顯示判斷來源編碼。
- 目前專案沒有測試專案，驗證主要依賴編譯與手動測試。
- `ManyToManyService` 使用通用的 `SourceTable / SourceId / TargetTable / TargetId` 關聯表，屬於泛型設計的良好範例，但目前沒有對應的 Controller。

---

## 9. 後續建議（依優先順序）

1. 補齊所有權、軟刪除重存與併發的可重現測試，先確認預期契約再調整行為（第 7 節）。
2. 評估交易結果 helper，保留通用 CRUD 與領域 API 不同的回應格式。
3. 評估 Id 來源 hook，減少 `PlayerController.GetModel` 的覆寫樣板。
4. 合併 `Save`／`SaveData` 的重複流程，並評估只選取主鍵的存在性查詢。
5. 簡化 `GetTableAttribute`，確認 `_ModelSettingService` 使用情況後再決定是否移除，評估欄位封裝。

以上建議僅為分析結論，本文件撰寫時未修改任何程式碼。

## 10. 前端遊戲存檔 API（遠端優先）

前端（reconstruct-nodes，Godot）以後端為準保存遊戲資料。本節記錄後端的對應實作。

### 10.1 共通規則

- 繼承 `BaseApiController` 的 API 在非 DEBUG 組建檢查 `Encode` header（`ApiFilter`）；DEBUG 直接略過。格式由 Library 的 [Encode.cs](../reconstruct-nodes-server.Library/Utility/Encode.cs) 定義，驗證拒絕早於伺服器目前時間超過 60 秒的值，沒有對未來時間設對稱上限；不要將其視為身分驗證。客戶端與伺服器應同步時鐘。
- 下表的領域與 Manager API 使用 `BackendApiResult<T>`（`Status`、`Success`、`MessageKey`、`MessageArgs`、`Data`）；`Success` 依 Status 是否為 2xx 設定。成功無提示的 key 為空字串、args 為空物件。參數為命名字串（例如 `cost`），由前端既有 LocalizedMessage 與語系資源翻譯；不傳例外文字或後端中文名稱。移除 `Message` 是破壞性契約更新，兩端需同步部署，不提供相容分支。通用 CRUD 與舊登入保留成功資料契約，失敗統一使用 key／args envelope。
- `IApiBusinessError` 帶出已知業務 key／args；交易中的未知例外記錄完整診斷並回 `backend.error.unexpected`，仍維持 HTTP 400。交易外未處理例外由 `ApiExceptionHandler` 回 HTTP 500，回應已開始時不改寫。MVC 模型驗證及無效 JSON 使用 `ApiValidationResponse` 回 HTTP 400；Filter 驗證失敗回 HTTP 401。
- 需要 Token 的 endpoint 以 `Authorization` header 傳入原始 GUID，沒有 Bearer 前綴；空字串時回退到 `PlayerToken`。DEBUG 無 header 時會使用程式中的固定測試 Token，仍須能查到玩家。驗證結果快取 10 分鐘。
- `ApiFilter`／`AuthorizeFilter` 拒絕時回 HTTP 401 與純文字，不使用 envelope；ASP.NET Core 模型繫結錯誤也不保證此 envelope。
- `ManagerController` 直接繼承 `BaseController`，不套用 `ApiFilter`；只有 session 查詢套用 `AuthorizeFilter`。

### 10.2 Endpoint

| Endpoint | 繼承基底 | 需要 Token | 說明 |
| --- | --- | --- | --- |
| `POST Manager/Login` | `BaseController` | 否 | Body `{Account, Password}`，驗證帳密後重置 Token，回傳 `Data {Token, Name}`；空帳密 400、驗證失敗 401。 |
| `POST Manager/GetSession` | `BaseController` + `AuthorizeFilter` | 是 | 回傳目前 Token 與 Name；目前只驗證玩家 Token，沒有額外的管理角色檢查。 |
| `POST DeviceAuth/Login` | `BaseApiController` | 否 | Body `{ClientId, Locale}`，找回或建立訪客帳號並重置 Token，回傳 `Data {Token, Name}`。 |
| `POST GoogleAuth/Login` | `BaseApiController` | 否 | Body `{Code, CodeVerifier, RedirectUri, Locale}`，桌面 PKCE 授權碼流程，回傳 `Data {Token, Name}`。規格見 `docs/GOOGLE_AUTH.md`。 |
| `POST GoogleAuth/LoginIdToken` | `BaseApiController` | 否 | Body `{IdToken, Locale}`，Android Credential Manager 的 ID Token 流程，以 `ServerClientId` 作為 audience，回傳 `Data {Token, Name}`。 |
| `POST Session/Validate` | `AuthorizeApiController` | 是 | 回傳 `Data {LoginKind, Name}`，`LoginKind` 為 `google`／`guest`／`other`，以 `IsClientLogin` 為權威來源。 |
| `POST GameProgress/Get` | `AuthorizeApiController` | 是 | 讀取遊戲進度；無資料回 404 envelope。 |
| `POST GameProgress/Save` | `AuthorizeApiController` | 是 | Body `GameProgressModel`，覆寫進度、部署與道路；不更動角色及其配置。部署使用 `character_id` 引用本人角色。 |
| `POST Character/GetList` | `AuthorizeApiController` | 是 | 讀取本人擁有角色，含目前武器與技能樹；交易內冪等補齊 kind=3 核心（空手、初始火球 kind=1），故新帳號至少回傳核心。 |
| `POST Character/Get?id=<GUID>` | `AuthorizeApiController` | 是 | 讀取本人單一角色；不存在或非本人角色回 404 envelope。 |
| `POST Character/Save` | `AuthorizeApiController` | 是 | Body `CharacterModel`，只更新已擁有角色的技能配置，保留權威武器引用；必須指定 `id` 並驗證本人及種類；成功 Data 為穩定角色 GUID。不能任意建立角色或穿卸裝。 |
| `POST Shop/GetCatalog` | `AuthorizeApiController` | 是 | 回傳抽卡價格與角色／碎片／基礎武器對應，規則由後端持有。 |
| `POST Shop/GetGold` | `AuthorizeApiController` | 是 | 回傳後端金幣餘額；新玩家初始 1200。 |
| `POST Shop/Draw` | `AuthorizeApiController` | 是 | Body `{request_id, count}`；只支援單抽與十連，後端抽選、扣款、建立角色及普通武器或發碎片，同一交易。 |
| `POST Talent/GetSnapshot` | `AuthorizeApiController` | 是 | 讀取天賦樹；無資料回 404 envelope。 |
| `POST Talent/Save` | `AuthorizeApiController` | 是 | Body `TalentModel`，以整份快照覆寫。 |
| `POST Item/GetInStockList` | `AuthorizeApiController` | 是 | 回傳本人 Status != -1 且 Amount > 0 的物品列；無資料回 200 空陣列。 |
| `POST Item/AdjustAmount` | `AuthorizeApiController` | 是 | Body `ItemDeltaRequest {ItemKey, Delta, Category?}`，對既有數量加上 `Delta`（可為負）。數量歸零即軟刪除。非冪等，見 10.3。 |
| `POST Item/Delete` | `AuthorizeApiController` | 是 | Body `ItemKeyRequest {ItemKey}`，軟刪除單一物品列。 |
| `POST Weapon/GetInstanceList` | `AuthorizeApiController` | 是 | 回傳武器列表，`Id` 等於 `EntryKey`；無資料回 200 空陣列。 |
| `POST Weapon/Acquire` | `AuthorizeApiController` | 是 | `{request_id, weapon}`，只取得新 EntryKey，回 `CommandReceipt<WeaponInstanceModel>`。 |
| `POST Weapon/Refine` | `AuthorizeApiController` | 是 | `{request_id, weapon_entry_id, crystal_id}`，原子扣材料及伺服器精煉，回 `CommandReceipt<WeaponRefineSnapshot>`。 |
| `POST Weapon/Disassemble` | `AuthorizeApiController` | 是 | `{request_id, weapon_entry_ids}`，去重批次分解、略過裝備中／不存在實例，回 `CommandReceipt<WeaponDisassembleSnapshot>`，無獎勵。 |
| `POST Character/EquipWeapon` | `AuthorizeApiController` | 是 | `{request_id, character_id, weapon_entry_id}`，null 卸下；只改武器引用，不寫技能，回 `CommandReceipt<CharacterModel>`。 |
| `POST AchievementProgress/Get` | `AuthorizeApiController` | 是 | 讀取成就進度；無群組資料回 404 envelope。 |
| `POST AchievementProgress/Save` | `AuthorizeApiController` | 是 | Body `AchievementProgressModel`，以整份快照覆寫。已領取的項目保留領取狀態，請求中的 `is_reward` 會被忽略。 |
| `POST AchievementProgress/ClaimReward` | `AuthorizeApiController` | 是 | Body `AchievementClaimRequest {request_id, expected_revision, achievement_item_id}`。`AchievementClaimCommand` 單次批次讀取、記憶體驗證（進度由伺服器資料推導）、單次批次寫入並 COMMIT：標記已領取、發放 `diamond` ×1、推進 revision 與寫回條；回 `CommandReceipt<AchievementClaimSnapshot {progress, items}>`，`items` 為領取後在庫物品，客戶端不必再呼叫 `Item/GetInStockList`。詳見 [PLAYER-ACTION-CONTRACTS](PLAYER-ACTION-CONTRACTS.md)。 |

上表端點皆為 `[HttpPost]`，包含讀取；不是整個專案所有 Action 都只接受 POST。

**URL 改名（破壞性變更，無相容 alias／redirect）**：為移除 Controller 名稱與 action 名稱的重複，下列舊 URL 已不再對應任何 action，新 URL 取代舊 URL。獨立的 Godot 前端（reconstruct-nodes，另一個 repo）的 API 呼叫路徑必須同步更新，否則會呼叫到不存在的 endpoint。路由映射由 `reconstruct-nodes-server.Tests/Controllers/ControllerRouteContractTests.cs` 以 endpoint routing 驗證。2026-10-12 啟動 Development 站台後，另以實際 HTTP 驗證下表十二個 Weapon、Talent、Item、GameProgress 與 AchievementProgress 新 URL：無 Token 的 POST 皆回 401、舊 URL 的 POST 皆回 404、新 URL 的 GET 皆回 405；未執行已授權的 action 或資料庫寫入。

| 舊 URL（POST） | 新 URL（POST） |
| --- | --- |
| `Weapon/GetWeaponList` | `Weapon/GetInstanceList` |
| `Weapon/SaveWeapon`、`Weapon/Save` | 已移除，取得使用 `Weapon/Acquire`；更新由明確業務 action 負責 |
| `Weapon/DeleteWeapon`、`Weapon/Delete` | 已移除，使用 `Weapon/Disassemble` |
| `Talent/GetTalent` | `Talent/GetSnapshot` |
| `Talent/SaveTalent` | `Talent/Save` |
| `Item/GetItemList` | `Item/GetInStockList` |
| `Item/AddItem` | `Item/AdjustAmount` |
| `Item/DeleteItem` | `Item/Delete` |
| `GameProgress/GetProgress` | `GameProgress/Get` |
| `GameProgress/SaveProgress` | `GameProgress/Save` |
| `AchievementProgress/GetProgress` | `AchievementProgress/Get` |
| `AchievementProgress/SaveProgress` | `AchievementProgress/Save` |
| `Manager/GetManagerSession` | `Manager/GetSession` |

`Manager/Login`、`DeviceAuth/*`、`GoogleAuth/*`、`Session/Validate`、`AchievementProgress/ClaimReward` 等未在此表中，URL 不變。

`/Login` 與 `Login/EmailVerification` 在交易成功時仍回傳舊的 `ResultModel` 狀態資料；交易失敗回 HTTP 400 key／args envelope，不再忽略失敗。訪客查找邏輯與 `DeviceAuth/Login` 共用 `PlayerService.FindOrCreateGuest`。

### 10.3 讀寫與冪等規則

- 讀取結果的區分：object 型 endpoint（`GameProgress`、`Talent`、`AchievementProgress`）無資料時回 HTTP 404，且 envelope 的 `Status` 為 404，代表「尚未建立」。不含 envelope 的 404（例如路由不存在）由客戶端視為讀取失敗，不可當成無資料。
- 清單型 endpoint（`Character`、`Item`、`Weapon`）無資料時回 200 空陣列。
- 寫入（`Save*`、`AdjustAmount`、`Item/Delete`、武器業務 action、`ClaimReward`）使用 Snapshot 交易、資源鍵及原子寫入；Character 與 GameProgress 以一致性快照避免讀到替換明細的中間狀態，不再使用玩家列鎖。精煉無外層交易時分成短快照讀取及版本條件提交。武器命令保留既有 envelope，預期資源衝突以 HTTP 503 / `backend.concurrency.retry` 回報，客戶端保留原 request ID。
- 冪等性：
  - `GameProgressCommand.Save`（端點 `GameProgress/Save`）為整份覆寫，重送安全。以 `Player` 列的 `UPDLOCK, HOLDLOCK` 序列化（與武器業務命令共用），再以實體 `DELETE`（非軟刪除）清除此玩家的子表列與 `game_progress*` 的 `PlayerParams` 列後重新寫入。進度沒有父列。
  - `TalentService.Save` 以 `UPDLOCK, HOLDLOCK` 鎖定父列後更新或新增，再實體刪除並重建子列。`AchievementProgressCommand.Save`（端點 `AchievementProgress/Save`）先鎖定群組並記錄已領取狀態，再實體刪除本人群組與子列後全部重建；保留仍存在之同名項目的領取狀態。這是領域快照覆寫，不保證重送後 DB Id／CreateDate 不變。
  - 武器寫入僅公開取得／精煉／批次分解 action，與 Character/EquipWeapon 使用資源寫入衝突及永久回執；不要求整域 checkpoint，禁止用舊 Save/Delete 繞過。詳細意圖比對、亂序回執與材料交易規則見 [WEAPON-CONTRACTS](WEAPON-CONTRACTS.md)。
  - `CharacterService.Save` 只更新本人已擁有的角色，保留角色父列 Id，整份替換該角色技能樹／節點。角色可未部署即可儲存；不改其他角色或進度，不能建立角色。
  - `ItemService.AdjustAmount`（端點 `Item/AdjustAmount`）非冪等。若網路逾時但伺服器已套用，重送會重複加上增量。客戶端應以 `Item/GetInStockList` 端點對帳，不應直接重送。
  - `ClaimReward` 以 `request_id` 冪等（相同意圖重播原回執），不重複發獎；以新 request ID 重領已領取項目回 400，而非再次回成功。
- 併發：武器儲存與成就領取以 `WITH (UPDLOCK, HOLDLOCK)` 鎖定既有列與鍵區間，避免並行重複插入或重複領取。
- 角色儲存、進度儲存與武器刪除共用 `Player` 列的 `UPDLOCK, HOLDLOCK`。角色裝備須引用本人有效武器，同一武器不可同時被不同角色裝備；進度部署須引用本人有效角色。角色讀取若發現懸空武器引用會明確拋錯，不偽裝成空裝備。
- 領域 Service 沒有自訂 1 MB payload 限制或版本上限；仍受 ASP.NET Core／部署平台的請求限制，以及 DTO 與 Service 的輸入檢查約束。例如 GameProgress 檢查座標與角色引用，Character 檢查節點型別與装備引用，Talent 檢查非負版本／點數，Achievement 檢查名稱與目標。
- JSON 欄位名稱：`Program.cs` 設定 `PropertyNamingPolicy = null`，以 C# 屬性名稱為準。`ItemDeltaRequest`、`ItemKeyRequest`、`EntryKeyRequest` 為 PascalCase；`AchievementClaimRequest` 以 `JsonPropertyName` 使用 `achievement_item_id`。其餘 DTO 以各自的屬性或 `JsonPropertyName` 為準。

### 10.4 資料表

每個儲存領域各自一組資料表，使用 `ModelBase` 的 `Id`、`Sequence`、`CreateDate`、`CreateUserId`、`Status`。領域主要列表過濾 `Status != -1`，但不能據此推論所有子表或通用單筆查詢都有相同條件。這批領域子表不建立資料庫外鍵；遊戲進度沒有 GameProgress 父表，Deployment 以 CharacterId 邏輯關聯獨立角色；角色技能樹與節點另有父子關聯與索引。整份覆寫時使用實體刪除後重寫。

| 領域 | 資料表 |
| --- | --- |
| 遊戲進度 | `Deployment`、`BattlefieldRoad`、`PlayerParams`（`game_progress` 與 `game_progress.*` 群組） |
| 擁有角色 | `Character`、`CharacterSkillTree`、`CharacterSkillNode`；角色以可空 `WeaponEntryKey` 引用庫存武器 |
| 天賦樹 | `Talent`、`TalentAcquired`、`TalentLegacyCategory` |
| 物品 | `Item`（`ItemKey`、`Amount`、`Category`） |
| 武器 | `Weapon`、`WeaponAffix`、`WeaponAffixValue`、`WeaponUniqueValue` |
| 成就 | `Achievement`（每群組一列，`Total` 為進度）、`AchievementDetail`（項目、`Target`、`IsReward`、`RewardDate`）、`AchievementCollected`（已收集項目的 `Name`，關聯 `AchievementId`） |

唯一索引（僅限未軟刪除的列）：`Item` 以 `(CreateUserId, ItemKey)`、`Weapon` 以 `(CreateUserId, EntryKey)`、`Character` 以 `(CreateUserId, Kind)`；角色非空的 `(CreateUserId, WeaponEntryKey)` 亦唯一，保證裝備排他。`Talent` 以 `CreateUserId`，確保每位玩家只有一列父資料。`PlayerParams` 沒有唯一索引，`game_progress*` 的 `(Group, Name)` 重複不會被資料庫擋下，讀取進度時會因字典鍵重複而失敗。

角色與部署的新契約：
- 角色 `id` 為穩定 GUID，`kind` 為種類，`name` 為名稱，`weapon_entry_id` 空字串表示卸下，`skill_trees` 為完整技能樹清單。只有 Shop/Draw 能建立擁有角色；Character/Save 拒絕未指定 Id 或尚未擁有的角色。裝備的其他輸出欄位由 Weapon 目前資料組出，不採信請求中的武器屬性。
- 每棵技能樹含 `kind`、`auxiliary_nodes`；節點 `type = auxiliary` 必須有 `loot_id`，`type = active` 必須有 `kind`。`auxiliary_gem_ids` 僅為推導輸出。
- 角色 `weapon_quality_version` 由 EquipWeapon 設為現行 2，卸下時清除；不採信 legacy Save 的武器引用或此中繼資料。
- GameProgress 移除 `character_weapons`；部署只含 `character_id`、`spot_index`、`wave` 與可選 `cell`，不再含角色名稱／種類／武器／技能快照。
- 前端需先載入角色清單，再依部署的 `character_id` 使用角色目前武器／技能。角色改裝會影響後續讀取的所有部署，不保存部署歷史配置。舊前端契約不相容。
- 解鎖群組仍是全域進度設定，不自動等同角色擁有資料；角色不必有 GameProgress 存檔才能儲存。

後端抽卡與金幣：
- 單抽 100 金幣、十連 900；戰士／弓箭手／法師等機率且每抽獨立，使用伺服器亂數，無保底。首次取得配發並裝備普通劍／弓／法杖，重複角色發對應碎片 ×1；同批十連亦會辨識剛取得的角色。
- 金幣仍依玩家保存於 `PlayerParams`（`Group = game_progress`、`Name = gold`、`Value` 為整數字串），沒有獨立金幣表。`GoldCommand` 負責解析／驗證餘額及後端交易，以該資源列原子更新與 Snapshot 衝突保護寫入；有效參數唯一鍵保護首次建立。GameProgress/Get 的 gold 由此列取得，Save 不寫入客戶端餘額，刪除進度列時亦排除 gold。通用 PlayerParams 的 gold 寫入／改名／刪除入口拒絕。
- `EconomyRequest` 以 `(CreateUserId, RequestId)` 唯一；同一抽卡 request_id 與相同次數重送時回原抽卡结果，不重新抽選、扣款或發獎。相同 request_id 用於不同操作或次數會被拒絕。
- 抽卡紀錄的 gold 是當次完成時餘額，重播後若要顯示最新餘額需再讀 Shop/GetGold，不可把歷史餘額當成目前餘額。
- ShopController 的 Snapshot 交易包住 GoldCommand 與 ShopCommand 的全部寫入，不取得玩家鎖；餘額不足、資源衝突或任何發放失敗均回滾。真 SQL 測試驗證同 request 不重扣、發獎中途失敗回滾，HTTP 驗證另涵蓋交易端點。
- `POST Economy/StartWave`：`{request_id,wave,difficulty}` → `{session_id,wave,enemy_count,gold}`。難度 0..3；有限模式波次 1..100，無限模式為正整數。敵人数為 `6 + wave * 3 + (wave % 5 == 0 ? 1 : 0)`。
- `POST Economy/ReportKill`：`{request_id,session_id,enemy_index}` → `{gold}`。只接受目前波次 session、索引範圍內的敵人；不同 request_id 不能對同敵人再次領獎。獎勵 `8 + wave`，每五波最後一敵為 BOSS，乘 8，再加已取得的金幣天賦加成。戰鬥仍由前端模擬，這是事件驗證／防重複，不是完整防作弊戰鬥驗證。
- `POST Economy/PurchaseSkill`：`{request_id,type,kind?,loot_id?}` → `{gold}`。`type` 為 `active` 或 `auxiliary`，每項 100，已解鎖不可再次購買；主動技能 kind 0..6、輔助技能限已知清單，Basic 0 無需購買。
- `GameProgress/Save` 每新增部署扣 60，新增部署需 `request_id`。以 `(spot_index,character_id,cell)` 判斷是否新增；既有部署不重扣，移除不退款。同 request_id 與相同完整 payload 重播不重扣，改 payload 拒絕。
- Character 技能配置需有解鎖權限；Basic 0 及各角色預設技能（戰士 5、弓手 0、法師 3）可直接配置，其他主動 root／節點及輔助節點必須先解鎖。進度快照不覆寫／清除技能解鎖群組；通用 PlayerParams CRUD 也禁止寫入、改名或刪除 gold、economy 與技能解鎖資料。
- 前端已同步新契約；角色擁有權以 Character 清單為準，抽卡、部署、技能購買與擊殺獎勵走後端交易。正式 AppShell 的 HTTP 測試涵蓋單抽／十連、碎片對帳、未部署角色配置、武器排他、部署扣款、擊殺獎勵、技能購買及重新載入；共 62 個斷言通過。舊本機抽卡與自由修改金幣的流程不相容。

本機資料庫套用與驗證：
- 已以 COPY_ONLY／CHECKSUM 備份並執行 RESTORE VERIFYONLY，再套用本次兩個 migrations。既有 3 筆 gold 的 checksum 與武器筆數保持不變，沒有 GoldAccount 表。
- opt-in SQL 測試：設定 `CHARACTER_SHOP_SQL_TESTS=1` 與 `CHARACTER_SHOP_SQL_CONNECTION`，執行 `dotnet test --filter TestCategory=CharacterShopSql`。只在允許測試的資料庫執行，測試以隨機玩家隔離並回滾／清理該玩家資料；不要提交連線憑證。

遊戲進度存於 `PlayerParams`，沒有父列：
- 純量：`Group = game_progress`，`Name` 為 `version`、`wave`、`gold`、`max_wave_reached`、`difficulty`、`lifetime_enemies_defeated`、`infinite_level`、`wave_completed`。戰場鍵 `battlefield_version`、`battlefield_columns`、`battlefield_rows`、`castle_x`、`castle_y` 只在有戰場時寫入。
- 設定：`Group = game_progress.<SettingGroup>`，`Name` 為設定鍵，`Value` 為原始 JSON，`Sequence` 保留順序。已清除波次為 `game_progress.cleared_waves`，`Name` 為索引。
- 寫入路徑：通用 `PlayerParams` CRUD（`PlayerParamsController`）拒絕 gold、economy 及技能解鎖相關資料，其他群組仍可直接寫入或刪除。通用路徑不經過 `ValidateProgress`，不保證進度一致性（`version` 缺失視為無存檔；缺少必要鍵或設定重複會使讀取失敗）。進度的正式寫入路徑仍是 `GameProgress/Save` 整份覆寫；後端權威金幣／解鎖資料另由交易服務維護。登入時的 `PlayerParamsList`（`PlayerService` 以 `GetModelList` 載入）包含這些列。
- 讀取時若沒有 `version`，`GameProgress/Get` 視為尚未建立（404）。

遷移：
- `20261010090110_InitialCreate` 建立初始資料表，包含舊 `PlayerSave`。
- `20261010130417_SplitSavesIntoDomainTables` 會刪除舊的 `PlayerSave` 表，表內資料不遷移。套用前須確認已備份或可捨棄。
- `20261010134726_AddItemNameAndCategory` 為 `Item` 新增 `Name`、`Category`。
- `20261013093548_RemoveStoredGameNames` 移除 `Character.Name`、`Item.Name`；Down 僅重建空欄位，無法恢復舊名稱。名稱由遊戲依 `kind`／`ItemKey` 翻譯，角色 DTO 不含 `name`，抽卡 DTO 不含 `character_name`，商店 Catalog 不含名稱，角色不再提供改名功能。`EconomyRequest.Result` 保留其他交易結果及冪等重送能力；舊 JSON 名稱會被新版 DTO 忽略，不改寫歷史紀錄。
- `20261011080434_MoveGameProgressToPlayerParams` 移除 `GameProgress`、`GameProgressSetting` 與 `GameProgressWeapon*` 四表。純量與設定移入 `PlayerParams`；裝備引用改存為 `GameProgressDeployment.WeaponEntryKey` 與新表 `CharacterWeapon`，部署另保存 `WeaponQualityVersion`。Up 不補建缺少的 Weapon 列，懸空 EntryKey 原樣保留並於讀取時清空。Down 重建舊表與搬回資料，武器快照取自回退時的 Weapon 資料，不是原始歷史快照的保證。
- `20261012061958_RenameGameProgressDeploymentTables` 將 `GameProgressDeployment`、`GameProgressDeploymentSkill`、`GameProgressDeploymentSkillNode`、`GameProgressBattlefieldRoad` 改名為 `Deployment`、`DeploymentSkill`、`DeploymentSkillNode`、`BattlefieldRoad`，並將 `DeploymentSkill.GameProgressDeploymentId` 與 `DeploymentSkillNode.GameProgressDeploymentSkillId` 改為 `DeploymentId`、`DeploymentSkillId`，同步改名索引與主鍵。只使用 RenameTable／RenameColumn／RenameIndex 與 sp_rename，不 Drop／Create，列資料原樣保留。Down 為反向改名。API 路由與 JSON 契約不變（DTO 名稱仍為 `GameProgress*Model`）。
- `20261013034518_DropLegacySettingAndCompleteTables` 刪除 16 張舊設定與完整資料鏈表（`AbilitySetting`、`AbilityCardSetting`、`AbilityProficiencySetting`、`MapSetting`、`MonsterSetting`、`Ability`、`AbilityCard`、`AbilitySelect`、`Character`、`Equipment`、`Modifier`、`Map`、`GameCollection`、`SettingVersion`、`AchievementSetting`、`AchievementDetailSetting`），並移除 `Achievement.AchievementSettingId`、`AchievementDetail.AchievementSettingId`、`AchievementDetail.AchievementDetailSettingId` 三欄。表內資料不保留；Down 只重建空表與三欄，不回復資料。
- `20261013044422_SeparateCharactersFromDeployments` 清除舊 Deployment 與角色分類的 Item 列，移除 CharacterWeapon、DeploymentSkill、DeploymentSkillNode，建立新的擁有角色及技能樹／節點表。部署移除角色／裝備副本，加入 CharacterId。保留武器庫存、戰場道路、金幣與進度參數，角色從新版商店重新取得。Down 亦清除 Deployment 並恢復空的舊 schema，無法回復歷史資料；套用前須備份並同步更新前端。
- `20261013051917_AddAuthoritativeGoldAndShopRequests` 僅建立 EconomyRequest，既有金幣原樣留在 PlayerParams，不搬移也不刪除。重複、無效或負數金幣會阻擋遷移，不靜默修正；無既有金幣的玩家由後端規則初始 1200。Down 只移除交易紀錄，金幣不變，回退後不能繼續使用原冪等紀錄。
