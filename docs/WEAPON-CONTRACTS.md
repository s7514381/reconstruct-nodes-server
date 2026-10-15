# 武器業務 action 契約

路由直接為 `/Weapon/...`、`/Character/...`（沒有 `/api` 前綴），均為授權後
POST，沿用既有 Authorization header、`BackendApiResult<T>`（`Data`／`Message` 等大小寫不變）。
前景等待／背景派送由客戶端管理；這些 API 沒有分類旗標，也不要求 `expected_revision`
或先保存整個 Weapon／Character／GameProgress checkpoint。

## 請求與回執

所有有副作用的請求必須包含非空 UUID `request_id`；同一意圖的未知提交／逾時重試
必須保留原 ID 與原輸入。資料層回執為 `CommandReceipt<T>`：
`{request_id: UUID, revision: int64, snapshot: T}`，位於 envelope 的 `Data`。

| 路由 | Request DTO／額外 JSON 欄位 | snapshot 型別／JSON |
|---|---|---|
| `/Weapon/GetInstanceList` | 無業務 body；查詢不需 request ID | `WeaponInstanceModel[]`（無 command receipt） |
| `/Weapon/Acquire` | `WeaponAcquireRequest`：`weapon: WeaponInstanceModel` | `WeaponInstanceModel` |
| `/Weapon/Refine` | `WeaponRefineRequest`：`weapon_entry_id: string, crystal_id: string` | `WeaponRefineSnapshot`：`{weapon, crystal_id, material_count: int}` |
| `/Weapon/Disassemble` | `WeaponDisassembleRequest`：`weapon_entry_ids: string[]` | `WeaponDisassembleSnapshot`：`{removed_ids, skipped_ids, weapons: WeaponInstanceModel[]}` |
| `/Character/EquipWeapon` | `WeaponEquipRequest`：`character_id: UUID, weapon_entry_id: string|null`；null／空字串卸下 | `CharacterModel` |

`WeaponInstanceModel` 保留既有 snake_case 格式：
`{id, weapon_id, weapon_family_id, rarity, affixes, unique}`；輸出的 `revision` 是伺服器
讀取版本；輸入的快照 revision 不作取得／精煉的 checkpoint。精煉不接受客戶端產生的結果。
分解至多接受 500 個輸入 ID，拒絕空白或超長 ID；去重並排序後作為集合意圖比對。
JSON 物件欄位順序不影響新業務命令的冪等比對；詞綴陣列順序仍保留。

## 權威與不變量

- `RevisionService.ExecuteIntent` 重用既有玩家 SQL row lock、`EconomyRequest` ledger 與
  request-scoped transaction；先查已保存回執，不作客戶端 revision fence。
  同 ID 同意圖只回原始回執，不再扣材料、擲骰、取得或刪除；同 ID 不同操作／意圖
  回 `backend.economy.request_conflict`。HTTP／envelope 錯誤分類沿用既有 controller。
- 取得只建立新 EntryKey。查重包含軟刪除資料，不能以另一個 request ID 覆寫既有武器，
  也不能復活已分解武器。Shop/Draw 已在自己的交易建立角色與初始普通武器，
  客戶端只套用其結果，不再呼叫 Acquire；核心角色初始化仍空手，不額外發武器。
- 精煉由現有 `RefinementRules` 驗證全部晶石、品質與詞綴規則，伺服器擲骰。
  `ItemService.AdjustAmount` 共用玩家鎖與 Item 鍵區間鎖，以 checked 整數增減；
  材料不得負數，不接受整包本機總量覆寫。扣款、武器寫入與回執在同一交易，
  任一步失敗全部回滾。裝備中的武器仍可精煉。
- 穿卸裝只更新 Character 的武器引用／quality metadata，不刪寫技能樹；
  驗證武器為本人有效持有、未被其他角色裝備。Character/Save 與
  Foreground/ConfigureSkills 繼續支援技能 consumers，但不能改變權威武器引用。
- 分解略過裝備中與不存在的 ID，回傳實際移除／略過集合，不發材料或金幣。
  與穿裝共用玩家交易鎖，避免檢查後的穿裝／刪除競態。
- `/Weapon/Save`、`/Weapon/Delete` 與 Foreground 下三個武器 action 已移除，
  不保留舊路由包裝；其他領域通用 Save／Delete 與技能配置未移除。
  `WeaponService.Save` 是內部可信 fixture／持久化用的舊低階入口，不再有公開 HTTP consumer。

## 生命週期與亂序

回執 revision 是原交易版本，不一定等於目前版本。重試 Acquire 即使原武器後來已分解，
仍回原始成功回執，**不會重新建立資料**。客戶端必須追蹤每個實例的確認／刪除狀態，
不能用晚到的舊 acquire 或完整 weapons 陣列覆蓋較新結果；必要時重新查詢。
裝備回執只應套用武器欄位，不能把舊回執的技能快照覆蓋新技能配置。
材料回執亦是交易當時餘額；同材料的取得／消耗在客戶端依實際資源順序確認或重新查詢，
不能以到達順序任意覆寫新餘額。後端交易安全不代表任意回應套用順序安全。

## 聚焦驗證

不連資料庫的測試（含 ledger replay、意圖衝突、路由清理、取得 tombstone、
精煉單次扣款、分解略過、技能隔離與既有 snapshot 行為）：

```powershell
dotnet build reconstruct-nodes-server.sln --no-restore -p:OutputPath=bin/WeaponRefactor/
dotnet test reconstruct-nodes-server.Tests --no-restore -p:OutputPath=bin/WeaponRefactor/ --filter "FullyQualifiedName~WeaponCommandTests|FullyQualifiedName~ForegroundRulesTests|FullyQualifiedName~SnapshotSaveTests|FullyQualifiedName~CoreCharacterTests|FullyQualifiedName~ApiMessageContractTests|FullyQualifiedName~ShopDrawTests|FullyQualifiedName~SnapshotPersistenceTests"
```

`ForegroundSqlTests` 包含真 SQL 原子回滾、精煉重試、分解保護、取得後分解重播不復活、
穿卸裝與技能隔離；`CoreHttpIntegrationTests` 已接到 Acquire／EquipWeapon。
這些 fixture 必須另行明確授權專用資料庫，並依 README 設定 opt-in；不得僅因修改測試
就連線遠端資料庫。隔離輸出不停止／覆寫正在執行的開發伺服器。
