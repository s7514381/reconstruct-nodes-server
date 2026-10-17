# 廣播系統（停機／公告）

伺服器以 Firebase Realtime Database（RTDB）發布廣播；遊戲端只讀公開節點，不經本伺服器、不持有金鑰。
前端契約與顯示規則見 `reconstruct-nodes/docs/broadcast-system.md`，兩邊須一起更新。

## 資料流

```
營運 ──POST /Broadcast/Publish (X-Admin-Key)──▶ 本伺服器 ──服務帳戶 PUT──▶ RTDB {BroadcastPath}/items/{id}
遊戲端 ──GET {BroadcastPath}/items.json (公開唯讀、ETag 輪詢)──▶ RTDB
```

## 設定與金鑰

| 項目 | 位置 | 說明 |
|---|---|---|
| 服務帳戶金鑰 | `Secret/firebase-adminsdk.json` | Admin 權限，略過 RTDB 規則；`Secret` 已在 `.gitignore`，**不得**複製到前端或提交 |
| 管理金鑰 | `Secret/broadcast-admin.json` → `Broadcast:AdminKey` | 請求標頭 `X-Admin-Key`；未設定時所有管理端點回 401 |
| 資料庫網址、路徑 | `appsettings*.json` → `Firebase:*` | 正式 `reconstruct-nodes/broadcast/production`；Development 為 `.../testing`，與前端 testing／production 環境對應 |

金鑰缺檔或 RTDB 不通只會讓管理端點回 503，不影響伺服器啟動。部署時 `Secret\firebase-adminsdk.json`、
`Secret\broadcast-admin.json` 與其他 Secret 檔一樣以 `PreserveNewest` 複製（見 csproj）。

## RTDB 規則（需在 Firebase Console 設定）

遊戲端無登入身分，只能靠公開讀取。寫入只由伺服器的服務帳戶完成，不需開放：

```json
{
  "rules": {
    "reconstruct-nodes": {
      "broadcast": { ".read": true, ".write": false }
    }
  }
}
```

此專案（`timwu19950429`）也供其他服務使用，請**合併**進現有規則，不要整份覆蓋。未設定時前端輪詢會得到 401，僅記錄警告、不影響遊戲。

## 管理端點（`BroadcastController`）

全部 `POST`，JSON 欄位大小寫不拘；回應沿用 `Status / Success / MessageKey / MessageArgs / Data`。

| 路由 | 請求 | 說明 |
|---|---|---|
| `/Broadcast/Publish` | `Id?`、`Kind`（`announcement`／`maintenance`）、`Title{zh_TW,en}`、`Body{zh_TW,en}`、`StartsAt`、`EndsAt`（UTC Unix 秒，`EndsAt=0` 不限） | 同 `Id` 覆寫並視為重新發布；省略 `Id` 自動產生。至少一個語系有標題；標題 ≤ 100、內文 ≤ 1000 字；`EndsAt` 須晚於 `StartsAt` 與現在 |
| `/Broadcast/Remove` | `Id` | 不存在回 404 |
| `/Broadcast/List` | 無 | 目前 RTDB 內全部項目（含尚未生效／已過期未清除者） |

錯誤：401 管理金鑰錯誤、400 `backend.broadcast.invalid`（`MessageArgs.field` 指出欄位）、
409 `backend.broadcast.limit_reached`（有效項目上限 20）、503 `backend.broadcast.unavailable`。
發布時會順帶刪除已過期項目。

範例（PowerShell，請從 Secret 讀金鑰，勿貼進紀錄）：

```powershell
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$body = @{ Kind = 'maintenance'; Title = @{ zh_TW = '停機維護'; en = 'Maintenance' }
  Body = @{ zh_TW = '23:00 起維護約 1 小時'; en = 'Maintenance from 23:00 for about 1 hour' }
  StartsAt = $now; EndsAt = $now + 3600 } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Uri https://<host>/Broadcast/Publish -Method Post -ContentType 'application/json; charset=utf-8' `
  -Body ([Text.Encoding]::UTF8.GetBytes($body)) -Headers @{ 'X-Admin-Key' = $key }
```

PowerShell 5.1 必須如上以 UTF-8 位元組送出，否則中文會變成 `?`。

## 邊界與限制

- 廣播只是**通知**：不會拒絕玩家請求。若要真正擋連線，需另在伺服器加維護旗標並回傳專屬 `MessageKey`。
- 廣播內文不是翻譯 key，由營運按語系填寫；只接受 `zh_TW`、`en`。
- RTDB 不保存空物件，沒有內文時 `body` 欄位不存在；前端已處理。

## 驗證

`dotnet test reconstruct-nodes-server.Tests --filter "FullyQualifiedName~BroadcastTests"`（記憶體 store，不連網）。
本機有後端程序鎖住輸出 DLL 時加 `--artifacts-path <暫存目錄>`。
