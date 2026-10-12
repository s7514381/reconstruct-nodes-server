# 專案架構分析：`_ModelService` 與 `BaseServiceApiController` 的複用設計

本文件以目前倉庫程式碼說明分層架構、共用方法、擴充點（hook）與 API 行為。文件同步不修改程式碼；以下行為依靜態實作核對，不代表已執行資料庫、併發或前端整合測試。

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
              │    ├─ AbilityController, AbilitySelectController, AchievementController,
              │    │  CharacterController, EquipmentController, GameCollectionController,
              │    │  MapController, PlayerController, PlayerParamsController
              ├─ BaseSettingApiController<DB, VM>   ← 設定類實體（AchievementSettingController）
              └─ GameProgressController, TalentController, ItemController,
                 WeaponController, AchievementProgressController ← 領域專用 API

  DeviceAuthController、LoginController 繼承 BaseApiController

Services
  BaseService                        （Dapper 封裝：QueryAsync / ExecuteAsync / InsertAsync / UpdateAsync / TransactionFunc）
    └─ _ModelService<DB, VM>         （SQL 組裝、CRUD、主從明細同步、Azure Table 轉移、Test/Seed 掛點）
         ├─ _SettingService<DB, VM>          （覆寫 SqlDataList，設定類實體的基底）
         │    ├─ SystemConfigService, SettingVersionService,
         │    │  AchievementSettingService, AchievementDetailSettingService
         ├─ _ModelSettingService<DB, VM, S, SM>   （目前無任何子類別，見第 7 節）
         └─ 一般實體 Service：AbilityService, AbilityCardService, AbilitySelectService,
            AchievementService, AchievementDetailService, CharacterService, EquipmentService,
            GameCollectionService, MapService, ModifierService, MonsterSettingService,
            ManyToManyService, PlayerService, PlayerParamsService, EmailVerifyService,
            ItemService
         └─ _SnapshotService<DB, Snapshot> （獨立快照 DTO 的 Save 與父表 upsert）
              └─ GameProgressService, TalentService, WeaponService, AchievementProgressService
```

資料流向：`前端 → HTTP → Controller → Service → BaseService → IDapperRepository → DapperRepository → SQL Server`。Controller 處理 HTTP、部分輸入檢查與交易邊界；領域 SQL 與業務規則主要位於 Service。EF Core 的 `ApplicationDbContext` 負責模型與 Migration，Dapper 負責上述 Service 的資料操作。

`Program.cs` 將 `IDapperRepository` 與公開、非抽象的 Argument／Service／Filter 類別註冊為 scoped；`Lazy<T>` 為 transient，`MailSender`、`AzureStoreService`、`FirebaseService` 為 singleton。`TimedHostedService` 的註冊目前被註解，啟動流程不會自動執行遷移。

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
| `DeleteOwnedRowsAsync(tableName, parentColumn?, parentId?)` | `protected` | 硬刪除本人擁有的列，可再以父鍵限定範圍。使用端：`WeaponService`、`TalentService`、`AchievementProgressService`、`GameProgressService` |
| `LockPlayerAsync()` | `protected` | 交易內以 `UPDLOCK, HOLDLOCK` 鎖定目前玩家列，供進度寫入與武器刪除共用 |
| `SqlDelete(subWhere)` | `public virtual` | 產生刪除用 SQL |

### 2.3 擴充點（Template Method hook）

獨立快照 DTO 不需要繼承 DBModel：`_SnapshotService<DBModel, Snapshot>` 沿用
`_ModelService<DBModel, DBModel>`，提供 `Save(Snapshot)`，先呼叫 `ValidateSnapshot`，
再呼叫 `SaveSnapshot`。Weapon、Talent、GameProgress、AchievementProgress 的公開 Save
皆為真正 override，委派基底執行此流程，客製化驗證及寫入位於對應 hooks。
Weapon 的詞綴與特殊屬性仍在 `SaveSnapshot` 開始時完整解析，早於任何資料庫操作。

`UpsertSnapshotParentAsync` 共用本人父列的 `UPDLOCK, HOLDLOCK` 查詢及新增／更新分支。
Weapon 使用 EntryKey 定位並提供限定欄位 UPDATE；Talent 以玩家定位並使用 UpdateAsync。
GameProgress 與 AchievementProgress 保留各自的整份覆寫策略，不使用父表 upsert。
所有快照儲存仍需由 Controller 的交易包裝呼叫，不在 Service 新增交易。

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

交易處理：寫入型端點皆透過 `BaseApiController.TransactionFunc` 包裝，成功回 `Ok()`，失敗回 `BadRequest()`。

例外是 DEBUG 的 `Seed`：直接呼叫 Service，不使用上述交易包裝。
`BaseSettingApiController` 提供 virtual `GetModelList` 與非 virtual、DEBUG 限定的 `Seed`。
`BaseApiController.DeleteByKeyAsync` 共用空鍵檢查、交易與 `BackendApiResult<int>` 回應，
供 `ItemController.Delete`、`WeaponController.Delete` 使用。

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
- `BaseService` 同時承擔資料存取與靜態連線數自動調校（`SemaphoreSlim` 替換與 `OptimalDBConnection`）。這是獨立的關注點，屬於後續可拆分的範圍。

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
| 1 | 通用 CRUD 端點重複交易與 Ok／BadRequest 樣板，未將 message 放入回應；領域 API 則會使用錯誤訊息 | `BaseServiceApiController.cs`、領域 Controller | 可評估共用 helper，但需保留兩種回應契約；刪除自然鍵已有 `DeleteByKeyAsync` |
| 2 | `GetData`、`GetModel`、`IsExist` 各自重複 `if (!request.Id.HasValue) return null;` | `BaseServiceApiController.cs` | 中：可抽成泛型 helper 或 `ResolveRequestId` hook |
| 3 | `PlayerController.GetModel` 整段覆寫只為改 Id 來源 | `PlayerController.cs` | 中：應改為 hook，子類別只覆寫 Id 來源 |
| 4 | `Save(IEnumerable)` 與 `SaveData(IEnumerable)` 的存在性查詢與分支邏輯完全相同，只差在 `UpdateModel`/`InsertModel` 與 `UpdateAsync`/`InsertAsync` | `ModelService.cs`（約第 172、199 行） | 中：可抽出 `private` 共用流程，以委派傳入差異 |
| 5 | 存在性查詢使用 `Sql()` 預設的 `SELECT main.*`，只需要 Id 卻取回整列 | `ModelService.cs` | 中：改為只選取 `_keyField`，需實機驗證 Dapper 行為 |
| 6 | 外鍵與主鍵解析集中在 `CommonUtility`；`BaseFunction` 與 `ModelSettingService` 使用共用工具 | `CommonUtility.cs`、`BaseFunction.cs` | 現況：不需再提出重複實作的修正建議 |
| 7 | `CommonUtility.GetTableAttribute<T>` 以迴圈遍歷屬性，但 `prop.ReflectedType` 永遠等於 `T`，迴圈無意義 | `CommonUtility.cs` | 低：可改為 `typeof(T).GetCustomAttribute<TableAttribute>()` 並回退到基底型別 |
| 8 | `Seed()` 與 `GetModelList()` 在 `BaseServiceApiController` 與 `BaseSettingApiController` 重複 | 兩個 Controller 基底 | 低：可抽出共用基底或共用邏輯 |
| 9 | `DeleteList` 檢查 `Count == 0`，但 `Delete` 不檢查，行為不一致 | `BaseServiceApiController.cs` | 低 |
| 10 | `GetModelListByParent` 宣告未使用的 `List<Task> tasks` | `ModelService.cs`（約第 120 行起） | 低：死碼 |
| 11 | `_ModelSettingService<DB, VM, S, SM>` 無任何子類別，其 `GetModelList` 覆寫也未被使用 | `Services/Base/ModelSettingService.cs` | 低：疑似死碼，需確認後再移除 |
| 12 | `_tableName`、`_keyField` 為 public 可變欄位；玩家資訊為 public 唯讀屬性 | `ModelService.cs` | 低：可評估縮小可見性 |

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
- **武器**：`WeaponService.Save` 以 `(CreateUserId, EntryKey)` 查既有列；API 的 `Id` 是 EntryKey，不是資料庫 GUID。不同玩家可有相同 EntryKey，各自寫入本人列。

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
- 下表的領域與 Manager API 使用 `BackendApiResult<T>`（`Status`、`Success`、`Message`、`Data`）；`Success` 依 Status 是否為 2xx 設定。通用 CRUD 與舊登入不全部使用此 envelope。
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
| `POST GameProgress/Save` | `AuthorizeApiController` | 是 | Body `GameProgressModel`，以整份快照覆寫（純量與設定存於 `PlayerParams`，其餘存於子表），子表一併重建。 |
| `POST Talent/GetSnapshot` | `AuthorizeApiController` | 是 | 讀取天賦樹；無資料回 404 envelope。 |
| `POST Talent/Save` | `AuthorizeApiController` | 是 | Body `TalentModel`，以整份快照覆寫。 |
| `POST Item/GetInStockList` | `AuthorizeApiController` | 是 | 回傳本人 Status != -1 且 Amount > 0 的物品列；無資料回 200 空陣列。 |
| `POST Item/AdjustAmount` | `AuthorizeApiController` | 是 | Body `ItemDeltaRequest {ItemKey, Delta, Name?, Category?}`，對既有數量加上 `Delta`（可為負）。數量歸零即軟刪除。非冪等，見 10.3。 |
| `POST Item/Delete` | `AuthorizeApiController` | 是 | Body `ItemKeyRequest {ItemKey}`，軟刪除單一物品列。 |
| `POST Weapon/GetInstanceList` | `AuthorizeApiController` | 是 | 回傳武器列表，`Id` 等於 `EntryKey`；無資料回 200 空陣列。 |
| `POST Weapon/Save` | `AuthorizeApiController` | 是 | Body `WeaponInstanceModel`，以其 `id` 作為 EntryKey，在本人名下 upsert；成功 Data 為該 id。 |
| `POST Weapon/Delete` | `AuthorizeApiController` | 是 | Body `EntryKeyRequest {EntryKey}`，軟刪除。仍被角色或部署裝備參照時回 400 envelope，不刪除。重複呼叫安全（冪等）。 |
| `POST AchievementProgress/Get` | `AuthorizeApiController` | 是 | 讀取成就進度；無群組資料回 404 envelope。 |
| `POST AchievementProgress/Save` | `AuthorizeApiController` | 是 | Body `AchievementProgressModel`，以整份快照覆寫。已領取的項目保留領取狀態，請求中的 `is_reward` 會被忽略。 |
| `POST AchievementProgress/ClaimReward` | `AuthorizeApiController` | 是 | Body `AchievementClaimRequest {achievement_item_id}`。在同一交易內標記已領取並發放 `diamond` ×1；伺服器信任已儲存的 `Total`，因此客戶端須先呼叫 `AchievementProgress/Save`。 |

上表端點皆為 `[HttpPost]`，包含讀取；不是整個專案所有 Action 都只接受 POST。

**URL 改名（破壞性變更，無相容 alias／redirect）**：為移除 Controller 名稱與 action 名稱的重複，下列舊 URL 已不再對應任何 action，新 URL 取代舊 URL。獨立的 Godot 前端（reconstruct-nodes，另一個 repo）的 API 呼叫路徑必須同步更新，否則會呼叫到不存在的 endpoint。路由映射由 `reconstruct-nodes-server.Tests/Controllers/ControllerRouteContractTests.cs` 以 endpoint routing 驗證。2026-10-12 啟動 Development 站台後，另以實際 HTTP 驗證下表十二個 Weapon、Talent、Item、GameProgress 與 AchievementProgress 新 URL：無 Token 的 POST 皆回 401、舊 URL 的 POST 皆回 404、新 URL 的 GET 皆回 405；未執行已授權的 action 或資料庫寫入。

| 舊 URL（POST） | 新 URL（POST） |
| --- | --- |
| `Weapon/GetWeaponList` | `Weapon/GetInstanceList` |
| `Weapon/SaveWeapon` | `Weapon/Save` |
| `Weapon/DeleteWeapon` | `Weapon/Delete` |
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

`/Login` 仍回傳舊的 `ResultModel`，供既有客戶端使用，格式不變。訪客查找邏輯與 `DeviceAuth/Login` 共用 `PlayerService.FindOrCreateGuest`。

### 10.3 讀寫與冪等規則

- 讀取結果的區分：object 型 endpoint（`GameProgress`、`Talent`、`AchievementProgress`）無資料時回 HTTP 404，且 envelope 的 `Status` 為 404，代表「尚未建立」。不含 envelope 的 404（例如路由不存在）由客戶端視為讀取失敗，不可當成無資料。
- 清單型 endpoint（`Item`、`Weapon`）無資料時回 200 空陣列。
- 寫入（`Save*`、`AdjustAmount`、`Item/Delete`、`Weapon/Delete`、`ClaimReward`）包在 `TransactionFunc` 交易內；交易失敗回 400 envelope（`Success = false`）。讀取不開交易。
- 冪等性：
  - `GameProgressService.Save`（端點 `GameProgress/Save`）為整份覆寫，重送安全。以 `Player` 列的 `UPDLOCK, HOLDLOCK` 序列化（與 `Weapon/Delete` 共用），再以實體 `DELETE`（非軟刪除）清除此玩家的子表列與 `game_progress*` 的 `PlayerParams` 列後重新寫入。進度沒有父列。
  - `TalentService.Save` 以 `UPDLOCK, HOLDLOCK` 鎖定父列後更新或新增，再實體刪除並重建子列。`AchievementProgressService.Save`（端點 `AchievementProgress/Save`）先鎖定群組並記錄已領取狀態，再實體刪除本人群組與子列後全部重建；保留仍存在之同名項目的領取狀態。這是領域快照覆寫，不保證重送後 DB Id／CreateDate 不變。
  - `WeaponService.Save` 依 `EntryKey` upsert，重送安全；`Weapon/Delete` 重複呼叫安全，但仍被裝備的武器會被拒絕（400）。
  - `ItemService.AdjustAmount`（端點 `Item/AdjustAmount`）非冪等。若網路逾時但伺服器已套用，重送會重複加上增量。客戶端應以 `Item/GetInStockList` 端點對帳，不應直接重送。
  - `ClaimReward` 不重複發獎；已領取再次呼叫回 400，而非再次回成功。
- 併發：武器儲存與成就領取以 `WITH (UPDLOCK, HOLDLOCK)` 鎖定既有列與鍵區間，避免並行重複插入或重複領取。
- 進度儲存與武器刪除以 `Player` 列的 `UPDLOCK, HOLDLOCK` 序列化，避免刪除武器與寫入裝備參照交錯。`GameProgressService.Save` 不驗證裝備的武器是否仍存在；讀取時若找不到有效武器，該槽位清空（不回傳懸空的 `EntryKey`）。懸空的 `EntryKey` 仍保留在資料庫，讀取不寫回，直到下一次整份覆寫才以空值寫入。
- 領域 Service 沒有自訂 1 MB payload 限制或版本上限；仍受 ASP.NET Core／部署平台的請求限制，以及 DTO 與 Service 的輸入檢查約束。例如 GameProgress 檢查座標與節點型別，Talent 檢查非負版本／點數，Achievement 檢查名稱與目標。
- JSON 欄位名稱：`Program.cs` 設定 `PropertyNamingPolicy = null`，以 C# 屬性名稱為準。`ItemDeltaRequest`、`ItemKeyRequest`、`EntryKeyRequest` 為 PascalCase；`AchievementClaimRequest` 以 `JsonPropertyName` 使用 `achievement_item_id`。其餘 DTO 以各自的屬性或 `JsonPropertyName` 為準。

### 10.4 資料表

每個儲存領域各自一組資料表，使用 `ModelBase` 的 `Id`、`Sequence`、`CreateDate`、`CreateUserId`、`Status`。領域主要列表過濾 `Status != -1`，但不能據此推論所有子表或通用單筆查詢都有相同條件。這批領域子表不建立資料庫外鍵；遊戲進度不再有 GameProgress 父表 Id，仍保留部署／技能間的關聯鍵；其餘領域保留父子關聯與索引。整份覆寫時使用實體刪除後重寫。

| 領域 | 資料表 |
| --- | --- |
| 遊戲進度 | `Deployment`、`DeploymentSkill`、`DeploymentSkillNode`、`BattlefieldRoad`、`CharacterWeapon`、`PlayerParams`（`game_progress` 與 `game_progress.*` 群組） |
| 天賦樹 | `Talent`、`TalentAcquired`、`TalentLegacyCategory` |
| 物品 | `Item`（`ItemKey`、`Amount`、`Name`、`Category`） |
| 武器 | `Weapon`、`WeaponAffix`、`WeaponAffixValue`、`WeaponUniqueValue` |
| 成就 | `Achievement`（每群組一列，`Total` 為進度）、`AchievementDetail`（項目、`Target`、`IsReward`、`RewardDate`）、`AchievementCollected`（已收集項目的 `Name`，關聯 `AchievementId`） |

唯一索引（僅限未軟刪除的列）：`Item` 以 `(CreateUserId, ItemKey)`、`Weapon` 以 `(CreateUserId, EntryKey)`、`CharacterWeapon` 以 `(CreateUserId, CharacterKind)`；`Talent` 以 `CreateUserId`，確保每位玩家只有一列父資料。`PlayerParams` 沒有唯一索引，`game_progress*` 的 `(Group, Name)` 重複不會被資料庫擋下，讀取進度時會因字典鍵重複而失敗。

遊戲進度存於 `PlayerParams`，沒有父列：
- 純量：`Group = game_progress`，`Name` 為 `version`、`wave`、`gold`、`max_wave_reached`、`difficulty`、`lifetime_enemies_defeated`、`infinite_level`、`wave_completed`。戰場鍵 `battlefield_version`、`battlefield_columns`、`battlefield_rows`、`castle_x`、`castle_y` 只在有戰場時寫入。
- 設定：`Group = game_progress.<SettingGroup>`，`Name` 為設定鍵，`Value` 為原始 JSON，`Sequence` 保留順序。已清除波次為 `game_progress.cleared_waves`，`Name` 為索引。
- 寫入路徑：通用 `PlayerParams` CRUD（`PlayerParamsController`）不限制群組，可直接寫入或刪除上述群組的列。此路徑不經過 `ValidateProgress`，也不取得 `Player` 列鎖，不保證進度一致性（`version` 缺失視為無存檔；缺少必要鍵或設定重複會使讀取失敗）。進度的正式寫入路徑仍是 `GameProgress/Save` 整份覆寫。登入時的 `PlayerParamsList`（`PlayerService` 以 `GetModelList` 載入）包含這些列。
- 讀取時若沒有 `version`，`GameProgress/Get` 視為尚未建立（404）。

遷移：
- `20261010090110_InitialCreate` 建立初始資料表，包含舊 `PlayerSave`。
- `20261010130417_SplitSavesIntoDomainTables` 會刪除舊的 `PlayerSave` 表，表內資料不遷移。套用前須確認已備份或可捨棄。
- `20261010134726_AddItemNameAndCategory` 為 `Item` 新增 `Name`、`Category`。
- `20261011080434_MoveGameProgressToPlayerParams` 移除 `GameProgress`、`GameProgressSetting` 與 `GameProgressWeapon*` 四表。純量與設定移入 `PlayerParams`；裝備引用改存為 `GameProgressDeployment.WeaponEntryKey` 與新表 `CharacterWeapon`，部署另保存 `WeaponQualityVersion`。Up 不補建缺少的 Weapon 列，懸空 EntryKey 原樣保留並於讀取時清空。Down 重建舊表與搬回資料，武器快照取自回退時的 Weapon 資料，不是原始歷史快照的保證。
- `20261012061958_RenameGameProgressDeploymentTables` 將 `GameProgressDeployment`、`GameProgressDeploymentSkill`、`GameProgressDeploymentSkillNode`、`GameProgressBattlefieldRoad` 改名為 `Deployment`、`DeploymentSkill`、`DeploymentSkillNode`、`BattlefieldRoad`，並將 `DeploymentSkill.GameProgressDeploymentId` 與 `DeploymentSkillNode.GameProgressDeploymentSkillId` 改為 `DeploymentId`、`DeploymentSkillId`，同步改名索引與主鍵。只使用 RenameTable／RenameColumn／RenameIndex 與 sp_rename，不 Drop／Create，列資料原樣保留。Down 為反向改名。API 路由與 JSON 契約不變（DTO 名稱仍為 `GameProgress*Model`）。
