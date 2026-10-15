# 前景原子 API 契約

本文保留的路由皆為授權後 `POST /Foreground/{action}`，沿用既有認證 header 與
`BackendApiResult<T>` envelope（大小寫不變）。本文欄位為 envelope 的資料內容。

武器 action 已移至 `/Weapon/Acquire`、`/Weapon/Refine`、`/Weapon/Disassemble`、
`/Character/EquipWeapon`，只需穩定 request ID、不需 expected_revision 或整域 checkpoint；
詳見 [WEAPON-CONTRACTS](WEAPON-CONTRACTS.md)。下述 revision 請求規則不適用這些 action。

## 版本與重試

- `Revisions` 無 body，回傳 `{talent,character,game_progress,achievement,weapon}`（int64）。
- 既有 Talent、Character、GameProgress、AchievementProgress、WeaponInstance 快照均新增
  `revision`（int64）、`expected_revision`（nullable int64）。既有 `version` 是內容版本，不是持久化版本。
- 版本化 legacy Save 成功時保留既有 body，另以 `X-State-Revision` response header 回傳該次
  保存實際 revision，無須改背景排程或為每次成功額外 GET。GameProgress request ID 重播也回原始保存 revision。
  讀取快照與 revision 使用同一 SQL transaction，避免回傳混合版本的父子表。
- 每個領域共用一個玩家級 revision epoch。Character／Weapon 的寫入 fence 細分到實例：
  對其他角色／武器的變更可安全 merge（命令讀取伺服器最新資料後僅改指定內容），
  對同一實例的過期快照／命令仍拒絕。不是以最大值覆蓋整份資料。
- 前景 command 必填 `request_id`（非空 UUID）、`expected_revision`（非負 int64）。
  第一次版本化寫入啟用該領域／實例 fence；之後 **對該狀態缺少版本的舊背景 Save 也拒絕**。
  尚未啟用的舊玩家仍可照原樣使用舊 Save。
- 衝突 `concurrency.revision_conflict`，arguments 為 `domain`（DTO 類名）及 `revision`（字串）。
  前景回 HTTP 409；舊 Save 保留原本錯誤 envelope。不得以 max 合併整份快照。
  衝突後重新讀取，再基於新狀態產生新的意圖／request ID；不可修改既有 request 的 expected revision。
- 相同 request ID 與相同完整 payload 重送，先重播永久保存的原始 receipt，再檢查版本；
  已有其他更新仍重播原結果，不重新花費／擲骰。相同 ID 改 payload 或操作為 409
  `backend.economy.request_conflict`。
- 成功資料一律 `{request_id,revision,snapshot}`；receipt 的 revision 是該操作提交時版本，
  可能早於目前版本。客戶端不能把舊 receipt 覆蓋相同領域／實例較新的已套用結果；
  不同角色的 receipt 需依 character ID 套用，不可因全域 epoch 較舊而丟掉另一角色的結果。
- SQL Player row lock、revision、業務更新、金幣／材料扣款及 receipt 使用同一 request-scoped
  SQL transaction；失敗全部回滾。版本與 receipt 利用既有 `PlayerParams`／`EconomyRequest`，
  不需新增 schema migration。`server.revisions` 不允許經通用 PlayerParams API 更改。

## 命令表

以下 request 除列出的欄位，均包含共同 `request_id,expected_revision`：

| Action | Request 欄位 | snapshot 型別 |
|---|---|---|
| `ConfigureSkills` | `character_id: UUID, skill_trees: GameProgressSkillLoadoutModel[]` | CharacterModel |
| `LearnTalent` | `tree_id: string, node_id: string` | TalentModel |
| `RefundTalent` | `tree_id: string, node_id: string`，退整個節點所有級數 | TalentModel |
| `ResetTalents` | `tree_id: string|null`，null 退全部樹 | TalentModel |
| `SetDeployment` | `deployment_log: GameProgressDeploymentModel[]` | GameProgressModel |
| `SetBattlefield` | `battlefield: GameProgressBattlefieldModel, deployment_log: GameProgressDeploymentModel[] 或 null` | `{progress: GameProgressModel, session: WaveStartResult}` |
| `TransitionWave` | `wave: int, difficulty: int, challenge: bool` | `{progress: GameProgressModel, session: WaveStartResult}` |
| `ClaimAchievement` | `achievement_item_id: string, snapshot: AchievementProgressModel` | AchievementProgressModel |

天賦命令從伺服器現有點數／進度執行，正式 180 節點的費用、級數、前置滿級及「前一段自身
投入至少 6 點」依客戶端 `TalentPointRegistry`／`ManaTalentPoint` 驗證；單點退款會檢查其餘
已學節點依賴。舊節點僅保留重置退款相容。

精煉材料以 Item 表為權威，非 GameProgress 的 refine_item_counts 鏡像；全部 11 晶石操作、
品質、詞綴家族唯一、前後綴額度、武器種類／等級、Tier 數值界線均由伺服器驗證及擲骰。
裝備中的武器可精煉；分解會略過裝備中／不存在的 ID、去重，**不發材料或金幣**（與客戶端一致）。

部署沿用既有新部署每個 60 金幣規則；伺服器讀取餘額，不接受客戶端餘額。
戰場編輯可同時指定部署，固定 7×10、2×2 城堡、道路連通與部署合法性均需通過；保存後重打目前波次。
手動選波 `challenge=true` 只允許第 1 波，否則僅可選該難度已完成的波次。
波次切換把 progress 與新 session 一次提交。切換後仍接受舊的、屬於同玩家 session 的合法
kill，保留 enemy index 邊界與每 session/index 一次獎勵限制。

商店抽角與技能購買仍使用現有 `/Shop/Draw`、`/Economy/PurchaseSkill`；
其 request ID 冪等與伺服器餘額規則不變，不建立第二套扣款。

## 聚焦驗證

```powershell
dotnet test reconstruct-nodes-server.Tests --filter FullyQualifiedName~ForegroundRulesTests
$env:FOREGROUND_SQL_TESTS='1'
dotnet test reconstruct-nodes-server.Tests --filter TestCategory=ForegroundSql
```

SQL 測試明確 opt-in，優先讀 `FOREGROUND_SQL_CONNECTION`，否則以 .NET ConfigurationBuilder
讀目前伺服器的 DefaultConnection（允許 JSON 註解）。僅可指向已授權 dedicated test DB。
每個測試產生獨立玩家並以交易回滾隔離，不接觸既有玩家。
唯一需跨兩連線提交的並行測試會在 finally 中依其新建 owner ID 精確刪除 fixture 資料。
