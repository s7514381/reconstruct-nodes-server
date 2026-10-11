# 專案架構分析：`_ModelService` 與 `BaseServiceApiController` 的複用設計

本文件說明 `reconstruct-nodes-server` 目前的分層架構，重點在 `_ModelService<DBModel, ViewModel>` 與 `BaseServiceApiController<DBModel, ViewModel>` 提供的可複用方法、擴充點（hook），並以強型別、SOLID 與物件導向原則檢視現況。文件僅記錄分析結果，**未修改任何程式碼**。

> 主要程式位於 `reconstruct-nodes-server/`（ASP.NET Core，net10.0）與 `reconstruct-nodes-server.Library/`（net10.0，共用模型與工具）。
>
> 對應前端為獨立倉庫 **`reconstruct-nodes`**（Godot 專案，與本後端倉庫分開維護），透過 HTTP 呼叫本後端的 API。

---

## 1. 分層總覽

```
HTTP 請求
  └─ BaseController                 （共用基底，含部分編碼異常的註解）
      └─ BaseApiController          （_logger、_lazy、TransactionFunc 交易包裝）
          └─ AuthorizeApiController （[ServiceFilter(AuthorizeFilter)] 驗證）
              ├─ BaseServiceApiController<DB, VM>   ← 一般實體的 CRUD 端點
              │    ├─ AbilityController, AbilitySelectController, AchievementController,
              │    │  CharacterController, EquipmentController, GameCollectionController,
              │    │  MapController, PlayerController, PlayerParamsController
              └─ BaseSettingApiController<DB, VM>   ← 設定類實體（僅 AchievementSettingController 使用）

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
            ManyToManyService, PlayerService, PlayerParamsService, EmailVerifyService
```

資料流向：`reconstruct-nodes（前端）→ HTTP → Controller → Service（_ModelService）→ BaseService → Dapper → 資料庫`。Controller 只負責 HTTP 轉換與交易邊界；SQL 與業務規則集中在 Service。

---

## 2. `_ModelService<DBModel, ViewModel>` 可複用方法

泛型參數約束：`DBModel : ModelBase, new()`、`ViewModel : DBModel, new()`。`ViewModel` 是 `DBModel` 的衍生型別，用來承載主檔加上子明細集合（例如 `AchievementModel.DetailList`）。

### 2.1 查詢

| 方法 | 可見性 | 說明 |
|---|---|---|
| `SqlData()` | `public virtual` | `SELECT * FROM {table} WHERE {key} = @Id` |
| `GetData(Guid)` | `public virtual` | 取得 DB 原始模型 |
| `GetModel(Guid)` | `public virtual` | 取得 ViewModel，並呼叫 `SetModel` hook 載入子資料 |
| `Sql(select, from, join, where, subWhere, order)` | `public virtual` | 組裝 SQL 的共用建構器，預設 `main.[Status] != -1`（軟刪除過濾） |
| `SqlDataList(subWhere)` | `public virtual` | 在 `Sql` 之上加上 `CreateUserId = @CreateUserId`（多租戶隔離） |
| `GetDataList()` | `public virtual` | 目前玩家的 DB 模型清單 |
| `GetModelList()` / `GetModelList(IEnumerable<Guid>)` | `public virtual` | 目前玩家的 ViewModel 清單（含子資料） |
| `SqlMTMDataList` / `GetMTMDataList` / `GetMTMModelList(sourceId)` | `public virtual` | 透過 `ManyToMany` 關聯表查詢 |
| `SetModelList(list)` | `public virtual` | 並行呼叫每筆的 `SetModel` |
| `GetModelListByParent<TParent>(parentId)` | `public virtual` | 以外鍵（由 `CommonUtility.GetForeignKeyField` 推導）取子清單，依 `Sequence` 排序 |
| `IsExist(Guid)` | `public virtual` | 存在性檢查 |

### 2.2 寫入

| 方法 | 可見性 | 說明 |
|---|---|---|
| `Save(ViewModel)` | `public virtual` | 單筆 upsert，委派給 `Save(IEnumerable)` |
| `Save(IEnumerable<ViewModel>)` | `public virtual` | 批次 upsert：先查本人既有 Id（僅限本人資料，見第 7.5 節），再分別呼叫 `UpdateModel` / `InsertModel`（會觸發 `ModelSave` hook） |
| `SaveData(DBModel)` / `SaveData(IEnumerable<DBModel>)` | `public virtual` | 與 `Save` 相同的 upsert 流程，但直接用 `UpdateAsync` / `InsertAsync`，**不觸發 hook** |
| `UpdateModel` / `InsertModel` | `protected virtual` | 寫入 `CreateUserId = _playerId`，呼叫 `ModelSave` 後寫入 |
| `UpdateDetail<TParent>(parent, list)` | `public virtual` | 主從明細同步（新增、更新、刪除差集；刪除僅限本人資料），外鍵由 `CommonUtility.GetForeignKeyField` 推導 |
| `DeleteModel(ViewModel)` / `DeleteModel(IEnumerable<ViewModel>)` | `public virtual` | 軟刪除（`Status = -1`）。僅處理 `CreateUserId` 為目前玩家的 Id，非本人 Id 靜默略過（與 `GetModelList` 一致）。會觸發 `ModelDelete` hook，並以背景工作同步到 Azure Table |
| `DeleteByNaturalKey(string naturalKey)` | `public virtual` | 依自然鍵軟刪除本人資料，回傳受影響筆數。子類別需要前置檢查時 override 並呼叫 base（例：`WeaponService` 先鎖定玩家並檢查是否仍被裝備） |
| `DeleteOwnedRowsAsync(tableName, parentColumn?, parentId?)` | `protected` | 硬刪除（`DELETE`，非軟刪除）本人擁有的子表列，可再以父鍵限定範圍。使用端：`WeaponService`、`TalentService`、`AchievementProgressService` |
| `SqlDelete(subWhere)` | `public virtual` | 產生刪除用 SQL |

### 2.3 擴充點（Template Method hook）

| Hook | 可見性 | 預設 | 典型用途 |
|---|---|---|---|
| `SetModel(ViewModel)` | `protected virtual` | 空 | 讀取後載入子資料，例：`AchievementService` 載入 `DetailList` |
| `ModelSave(ViewModel)` | `protected virtual` | 空 | 寫入前後處理子明細，例：`AchievementService` 呼叫 `UpdateDetail` |
| `ModelDelete(ViewModel)` | `protected virtual` | 空 | 刪除前後的連動處理 |
| `NaturalKeyField` | `protected virtual` | `null` | 自然鍵欄位名稱（`WeaponService`：`EntryKey`；`ItemService`：`ItemKey`），供 `DeleteByNaturalKey` 使用；未設定時拋出 `InvalidOperationException` |
| `SqlDataList(subWhere)` | `public virtual` | 加上 `CreateUserId` | 設定類實體改寫過濾條件（`_SettingService`） |
| `Seed()` | `public virtual` | 空 | 種子資料（`BaseServiceApiController` 僅在 DEBUG 呼叫；`BaseSettingApiController` 無守衛，見第 7.1 節） |
| `TestSave()` / `TestSaveList()` / `TestDelete()` | `public virtual` | 空 | 測試用掛點（DEBUG 端點呼叫） |
| `AzureTableTransfer(...)` | `public` | — | 將資料轉移到 Azure Table，接受委派 `Func<_ModelService, TableEntity, Task>` |

---

## 3. `BaseServiceApiController<DBModel, ViewModel>` 端點

建構子：`BaseServiceApiController(BaseApiArgument argument, _ModelService<DBModel, ViewModel> service)`。所有端點皆為 `[HttpPost]`，且標為 `virtual`，子類別可以覆寫。

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

### 3.1 子類別覆寫情況

- `PlayerController` 覆寫 `GetModel`，改用 `service._playerId` 作為 Id 來源，而不使用 `request.Id`（[PlayerController.cs](../reconstruct-nodes-server/Controllers/PlayerController.cs) 第 18 行附近）。這是把「Id 來源」寫死在子類別，導致流程重複（見第 6 節）。
- `AchievementController` 在自身程式碼中手動呼叫 `TransactionFunc`（第 27 行附近），未使用 `BaseServiceApiController` 已有的流程。

---

## 4. 新增一般實體的標準步驟

在現有架構下，新增一個標準 CRUD 實體只需兩個類別，不需撰寫任何端點或 SQL：

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

Step 1：在 `Library/DBModel` 定義 `Xxx`（含 `[Table]`、`[Key]` 屬性，供 `CommonUtility.GetTableAttribute` / `GetKeyAttribute` 讀取）。
Step 2：在 `Library/GameModel` 定義 `XxxModel : Xxx`（ViewModel）。
Step 3：建立上面兩個類別。
Step 4：依需要覆寫 `SetModel`、`ModelSave`、`ModelDelete`，或在 Controller 覆寫端點。

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

- **`AchievementService.SaveData(AchievementModel)`**：這是同名但非 `override` 的多載，會繞過 `ModelSave` hook。`BaseServiceApiController.SaveData` 以 `DBModel` 靜態型別呼叫，因此實際上**不會**進入此多載，疑似失效程式碼。
- **`SystemConfigService.Seed()`**（已修正）：原先隱藏基底的 `virtual Seed()`，現已改為 `override`。

### 5.6 ISP（介面隔離）

- 專案未定義介面，`_ModelService` 是單一大型抽象類別，承擔所有 CRUD、明細、測試與種子功能。子類別被迫繼承全部成員（即使不需要 `Test*` 或 `AzureTableTransfer`）。
- 若日後需要拆分，可依用途區分為「讀取」、「寫入」、「測試/種子」三組介面，但目前不建議為此導入介面體系。

### 5.7 封裝與強型別

- `_tableName`、`_keyField` 為 `public` 可變欄位，`_playerId`、`_player` 亦為 `public`。外部可任意修改，建議改為 `protected` 或 `readonly`（`_tableName` / `_keyField` 僅在建構子設定）。
- `Save` 與 `SaveData` 的成對方法依靠 `DBModel` / `ViewModel` 的靜態型別分流，這是一個合理的強型別設計，但也造成第 6 節所述的重複。

---

## 6. 重複與待改善項目（依影響排序）

| # | 項目 | 位置 | 影響 |
|---|---|---|---|
| 1 | `(bool success, string? message) = await TransactionFunc(...)` + `if (success) Ok() else BadRequest()` 在 `BaseServiceApiController` 內重複 9 次，另 `AchievementController`、`LoginController` 各有 1–2 次，`message` 從未使用 | `BaseServiceApiController.cs`、`AchievementController.cs`、`LoginController.cs` | 高：重複樣板，應抽成共用 helper（例如 `BaseApiController` 內的 `ExecuteTransactionAsync(Func<Task>)` 回傳 `IActionResult`） |
| 2 | `GetData`、`GetModel`、`IsExist` 各自重複 `if (!request.Id.HasValue) return null;` | `BaseServiceApiController.cs` | 中：可抽成泛型 helper 或 `ResolveRequestId` hook |
| 3 | `PlayerController.GetModel` 整段覆寫只為改 Id 來源 | `PlayerController.cs` | 中：應改為 hook，子類別只覆寫 Id 來源 |
| 4 | `Save(IEnumerable)` 與 `SaveData(IEnumerable)` 的存在性查詢與分支邏輯完全相同，只差在 `UpdateModel`/`InsertModel` 與 `UpdateAsync`/`InsertAsync` | `ModelService.cs`（約第 172、199 行） | 中：可抽出 `private` 共用流程，以委派傳入差異 |
| 5 | 存在性查詢使用 `Sql()` 預設的 `SELECT main.*`，只需要 Id 卻取回整列 | `ModelService.cs` | 中：改為只選取 `_keyField`，需實機驗證 Dapper 行為 |
| 6 | `GetForeignKeyField` 與 `GetKeyAttribute` 在 `Library/Utility/CommonUtility.cs` 與 `Model/BaseFunction.cs` 各有一份，幾乎相同 | `CommonUtility.cs`、`BaseFunction.cs` | 已解決：`CommonUtility` 為單一來源，`BaseFunction`、`ModelSettingService` 改為委派 |
| 7 | `CommonUtility.GetTableAttribute<T>` 以迴圈遍歷屬性，但 `prop.ReflectedType` 永遠等於 `T`，迴圈無意義 | `CommonUtility.cs` | 低：可改為 `typeof(T).GetCustomAttribute<TableAttribute>()` 並回退到基底型別 |
| 8 | `Seed()` 與 `GetModelList()` 在 `BaseServiceApiController` 與 `BaseSettingApiController` 重複 | 兩個 Controller 基底 | 低：可抽出共用基底或共用邏輯 |
| 9 | `DeleteList` 檢查 `Count == 0`，但 `Delete` 不檢查，行為不一致 | `BaseServiceApiController.cs` | 低 |
| 10 | `GetModelListByParent` 宣告未使用的 `List<Task> tasks` | `ModelService.cs`（約第 120 行起） | 低：死碼 |
| 11 | `_ModelSettingService<DB, VM, S, SM>` 無任何子類別，其 `GetModelList` 覆寫也未被使用 | `Services/Base/ModelSettingService.cs` | 低：疑似死碼，需確認後再移除 |
| 12 | `_tableName`、`_keyField`、`_playerId`、`_player` 為 `public` | `ModelService.cs` | 低：封裝問題 |

---

## 7. 已知風險與行為不一致

### 7.1 `Seed` 端點的 DEBUG 守衛不一致（安全相關）

- `BaseServiceApiController` 的 `Seed()` 位於 `#if DEBUG` 之內，Release 組建不會暴露此端點。
- `BaseSettingApiController` 的 `Seed()` **沒有** `#if DEBUG` 守衛，因此 `AchievementSettingController` 在 Release 組建也會暴露 `Seed` 端點。

這個差異可能是疏漏。若 `Seed` 會寫入資料，在正式環境可被呼叫會有風險。是否要統一加上 DEBUG 守衛，需由維護者決定，因為這會改變 Release 的行為。

### 7.2 `SaveData` 的 hook 略過

`SaveData` 系列不會呼叫 `ModelSave`，`Save` 系列會。兩者在呼叫端的選擇會影響是否觸發子明細同步。`BaseServiceApiController` 同時提供兩者，呼叫者需要自行判斷，這是潛在的誤用來源。

### 7.3 軟刪除與主鍵衝突（部分驗證）

`Sql()` 預設過濾 `Status != -1`，因此已軟刪除的列在存在性檢查中會被視為「不存在」，接著 `Save` 會走 `InsertModel`，與既有主鍵衝突。

已驗證：存在性檢查限定本人後，他人 Id 走 insert 路徑會觸發 PK 衝突並回滾（角色存檔測試 C1，見第 7.5 節）。尚未直接驗證的部分：本人已軟刪除的列以相同 Id 再次 `Save`。

### 7.4 `SystemConfigService.Seed()` 隱藏基底方法（已修正）

已修正：現已使用 `override`，此問題不再存在。

### 7.5 存檔時的擁有權處理（待使用者確認）

`Save` / `SaveData` 的存在性查詢（`GetExistIdSet`）已限定為目前玩家（`CreateUserId`），避免以他人的 Id 覆寫資料。查無此列時會改走新增路徑，結果依實體而不同：

- **角色**：他人的 Id 被視為不存在，走 `InsertModel`。主鍵只有 `Id`，因此觸發 PK 衝突，交易回滾並回傳 400。已以測試驗證（C1）。
- **武器**：`SaveWeapon` 以 `(CreateUserId, EntryKey)` 查既有列，不比對 Id。`EntryKey` 屬於他人時，會在本人名下新增一筆同 `EntryKey` 的列，他人的列不變。已以測試驗證（W9）。

武器與道具的刪除路徑則靜默略過他人的 Id（回傳 `data = 0`）。是否統一為「他人 Id 一律拒絕」，需由維護者決定。

---

## 8. 編碼與其他觀察

- 部分檔案含 UTF-8 BOM，`Controllers/Base/BaseController.cs` 的部分中文註解出現亂碼（非 UTF-8 來源），編輯時需保留原始編碼，避免註解再次損壞。
- 目前專案沒有測試專案，驗證主要依賴編譯與手動測試。
- `ManyToManyService` 使用通用的 `SourceTable / SourceId / TargetTable / TargetId` 關聯表，屬於泛型設計的良好範例，但目前沒有對應的 Controller。

---

## 9. 後續建議（依優先順序）

1. 確認 `Seed` 端點在 Release 的暴露策略，並統一 DEBUG 守衛（第 7.1 節）。
2. 移除或修正 `AchievementService.SaveData(AchievementModel)` 與 `SystemConfigService.Seed` 的隱藏問題（第 5.5 節）。
3. 在 `BaseApiController` 抽出交易結果 helper，取代重複的交易樣板（第 6 節第 1 項）。
4. 在 `BaseServiceApiController` 新增 Id 來源的 hook，移除 `PlayerController.GetModel` 的覆寫（第 6 節第 2、3 項）。
5. 合併 `Save` / `SaveData` 的核心流程為單一 `private` 方法（第 6 節第 4、5 項）。
6. （已完成）統一 `GetForeignKeyField` / `GetKeyAttribute` 到 `CommonUtility`（第 6 節第 6 項）。第 7 項 `GetTableAttribute` 仍待處理。
7. 確認 `_ModelSettingService` 無使用後移除，並將 `_tableName` / `_keyField` 等欄位封裝（第 6 節第 11、12 項）。

以上建議僅為分析結論，本文件撰寫時未修改任何程式碼。

## 10. 前端遊戲存檔 API（遠端優先）

前端（reconstruct-nodes，Godot）以後端為準保存遊戲資料。本節記錄後端的對應實作。

### 10.1 共通規則

- 所有 endpoint 都必須帶 `Encode` header（`ApiFilter`），格式與 `Utility/Encode.New()` 相同，時間窗 60 秒。伺服器與客戶端需要時鐘同步。
- 回應 envelope 為 `BackendApiResult<T>`（`Status`、`Success`、`Message`、`Data`），定義於 `reconstruct-nodes-server.Library`。
- 需要 Token 的 endpoint 以 `Authorization` header 傳入原始 GUID（`AuthorizeFilter`）。

### 10.2 Endpoint

| Endpoint | 繼承基底 | 需要 Token | 說明 |
| --- | --- | --- | --- |
| `POST DeviceAuth/Login` | `BaseApiController` | 否 | Body `{ClientId, Locale}`，找回或建立訪客帳號並重置 Token，回傳 `Data {Token, Name}`。 |
| `POST GameProgress/GetProgress` | `AuthorizeApiController` | 是 | 讀取遊戲進度；無資料回 404 envelope。 |
| `POST GameProgress/SaveProgress` | `AuthorizeApiController` | 是 | Body `GameProgressModel`，以整份快照覆寫（純量與設定存於 `PlayerParams`，其餘存於子表），子表一併重建。 |
| `POST Talent/GetTalent` | `AuthorizeApiController` | 是 | 讀取天賦樹；無資料回 404 envelope。 |
| `POST Talent/SaveTalent` | `AuthorizeApiController` | 是 | Body `TalentModel`，以整份快照覆寫。 |
| `POST Item/GetItemList` | `AuthorizeApiController` | 是 | 回傳玩家所有物品列；無資料回 200 空陣列。 |
| `POST Item/AddItem` | `AuthorizeApiController` | 是 | Body `ItemDeltaRequest {ItemKey, Delta, Name?, Category?}`，對既有數量加上 `Delta`（可為負）。數量歸零即軟刪除。非冪等，見 10.3。 |
| `POST Item/DeleteItem` | `AuthorizeApiController` | 是 | Body `ItemKeyRequest {ItemKey}`，軟刪除單一物品列。 |
| `POST Weapon/GetWeaponList` | `AuthorizeApiController` | 是 | 回傳武器列表，`Id` 等於 `EntryKey`；無資料回 200 空陣列。 |
| `POST Weapon/SaveWeapon` | `AuthorizeApiController` | 是 | Body `WeaponInstanceModel`，依 `EntryKey` upsert。 |
| `POST Weapon/DeleteWeapon` | `AuthorizeApiController` | 是 | Body `EntryKeyRequest {EntryKey}`，軟刪除。仍被角色或部署裝備參照時回 400 envelope，不刪除。重複呼叫安全（冪等）。 |
| `POST AchievementProgress/GetProgress` | `AuthorizeApiController` | 是 | 讀取成就進度；無群組資料回 404 envelope。 |
| `POST AchievementProgress/SaveProgress` | `AuthorizeApiController` | 是 | Body `AchievementProgressModel`，以整份快照覆寫。已領取的項目保留領取狀態，請求中的 `is_reward` 會被忽略。 |
| `POST AchievementProgress/ClaimReward` | `AuthorizeApiController` | 是 | Body `AchievementClaimRequest {achievement_item_id}`。在同一交易內標記已領取並發放 `diamond` ×1；伺服器信任已儲存的 `Total`，因此客戶端須先 `SaveProgress`。 |

所有 endpoint 皆為 `[HttpPost]`，包含讀取。

`/Login` 仍回傳舊的 `ResultModel`，供既有客戶端使用，格式不變。訪客查找邏輯與 `DeviceAuth/Login` 共用 `PlayerService.FindOrCreateGuestPlayer`。

### 10.3 讀寫與冪等規則

- 讀取結果的區分：object 型 endpoint（`GameProgress`、`Talent`、`AchievementProgress`）無資料時回 HTTP 404，且 envelope 的 `Status` 為 404，代表「尚未建立」。不含 envelope 的 404（例如路由不存在）由客戶端視為讀取失敗，不可當成無資料。
- 清單型 endpoint（`Item`、`Weapon`）無資料時回 200 空陣列。
- 寫入（`Save*`、`AddItem`、`DeleteItem`、`DeleteWeapon`、`ClaimReward`）包在 `TransactionFunc` 交易內；交易失敗回 400 envelope（`Success = false`）。讀取不開交易。
- 冪等性：
  - `SaveProgress` 為整份覆寫，重送安全。以 `Player` 列的 `UPDLOCK, HOLDLOCK` 序列化（與 `DeleteWeapon` 共用），再以實體 `DELETE`（非軟刪除）清除此玩家的子表列與 `game_progress*` 的 `PlayerParams` 列後重新寫入。進度沒有父列。
  - `SaveTalent`、`AchievementProgress/SaveProgress` 為整份覆寫，重送安全。父列以 `UPDLOCK, HOLDLOCK` 鎖定後更新或新增；子表以實體 `DELETE`（非軟刪除）清除後重新寫入。
  - `SaveWeapon` 依 `EntryKey` upsert，重送安全；`DeleteWeapon` 重複呼叫安全，但仍被裝備的武器會被拒絕（400）。
  - `AddItem` 非冪等。若網路逾時但伺服器已套用，重送會重複加上增量。客戶端應以 `GetItemList` 對帳，不應直接重送。
- 併發：武器儲存與成就領取以 `WITH (UPDLOCK, HOLDLOCK)` 鎖定既有列與鍵區間，避免並行重複插入或重複領取。
- 進度儲存與武器刪除以 `Player` 列的 `UPDLOCK, HOLDLOCK` 序列化，避免刪除武器與寫入裝備參照交錯。`SaveProgress` 不驗證裝備的武器是否仍存在；讀取時若找不到有效武器，該槽位清空（不回傳懸空的 `EntryKey`）。懸空的 `EntryKey` 仍保留在資料庫，讀取不寫回，直到下一次整份覆寫才以空值寫入。
- 本系列 endpoint 沒有 payload 大小或版本上限。舊 `GameSaveService` 的 1 MB 與版本上限已隨之移除，結構只由 DTO 約束。
- JSON 欄位名稱：`Program.cs` 設定 `PropertyNamingPolicy = null`，以 C# 屬性名稱為準。`ItemDeltaRequest`、`ItemKeyRequest`、`EntryKeyRequest` 為 PascalCase；`AchievementClaimRequest` 以 `JsonPropertyName` 使用 `achievement_item_id`。其餘 DTO 以各自的屬性或 `JsonPropertyName` 為準。

### 10.4 資料表

每個儲存領域各自一組資料表。所有表共用 `ModelBase` 欄位（`Id`、`Sequence`、`CreateDate`、`CreateUserId`、`Status`）。父列的軟刪除以 `Status = -1` 表示，查詢皆以 `Status != -1` 過濾。子表不建立資料庫外鍵；遊戲進度的子表改以 `CreateUserId` 歸屬，不再有父表 Id，其餘子表仍以父表 Id 建立索引。整份覆寫時子表列會被實體刪除後重寫，不走軟刪除。

| 領域 | 資料表 |
| --- | --- |
| 遊戲進度 | `GameProgressDeployment`、`GameProgressDeploymentSkill`、`GameProgressDeploymentSkillNode`、`GameProgressBattlefieldRoad`、`CharacterWeapon`、`PlayerParams`（`game_progress` 與 `game_progress.*` 群組） |
| 天賦樹 | `Talent`、`TalentAcquired`、`TalentLegacyCategory` |
| 物品 | `Item`（`ItemKey`、`Amount`、`Name`、`Category`） |
| 武器 | `Weapon`、`WeaponAffix`、`WeaponAffixValue`、`WeaponUniqueValue` |
| 成就 | `Achievement`（每群組一列，`Total` 為進度）、`AchievementDetail`（項目、`Target`、`IsReward`、`RewardDate`）、`AchievementCollected`（已收集項目的 `Name`，關聯 `AchievementId`） |

唯一索引（僅限未軟刪除的列）：`Item` 以 `(CreateUserId, ItemKey)`、`Weapon` 以 `(CreateUserId, EntryKey)`、`CharacterWeapon` 以 `(CreateUserId, CharacterKind)`；`Talent` 以 `CreateUserId`，確保每位玩家只有一列父資料。`PlayerParams` 沒有唯一索引，`game_progress*` 的 `(Group, Name)` 重複不會被資料庫擋下，讀取進度時會因字典鍵重複而失敗。

遊戲進度存於 `PlayerParams`，沒有父列：
- 純量：`Group = game_progress`，`Name` 為 `version`、`wave`、`gold`、`max_wave_reached`、`difficulty`、`lifetime_enemies_defeated`、`infinite_level`、`wave_completed`。戰場鍵 `battlefield_version`、`battlefield_columns`、`battlefield_rows`、`castle_x`、`castle_y` 只在有戰場時寫入。
- 設定：`Group = game_progress.<SettingGroup>`，`Name` 為設定鍵，`Value` 為原始 JSON，`Sequence` 保留順序。已清除波次為 `game_progress.cleared_waves`，`Name` 為索引。
- 寫入路徑：通用 `PlayerParams` CRUD（`PlayerParamsController`）不限制群組，可直接寫入或刪除上述群組的列。此路徑不經過 `ValidateProgress`，也不取得 `Player` 列鎖，不保證進度一致性（`version` 缺失視為無存檔；缺少必要鍵或設定重複會使讀取失敗）。進度的正式寫入路徑仍是 `GameProgress/SaveProgress` 整份覆寫。登入時的 `PlayerParamsList`（`PlayerService` 以 `GetModelList` 載入）包含這些列。
- 讀取時若沒有 `version`，`GetProgress` 視為尚未建立（404）。

遷移：
- `20261010090110_InitialCreate` 保持不變。
- `20261010130417_SplitSavesIntoDomainTables` 會刪除舊的 `PlayerSave` 表，表內資料不遷移。套用前須確認已備份或可捨棄。
- `20261010134726_AddItemNameAndCategory` 為 `Item` 新增 `Name`、`Category`。
- `20261011080434_MoveGameProgressToPlayerParams` 移除 `GameProgress` 父表與 `GameProgressWeapon*` 四表。純量與設定移入 `PlayerParams`；裝備改存為 `GameProgressDeployment.WeaponEntryKey` 與新表 `CharacterWeapon`，只保存 `EntryKey`。Up 只搬移既有資料，不補建缺少的 `Weapon` 列，懸空的 `EntryKey` 原樣保留並於讀取時清空（見 10.2）。Down 會還原進度。
