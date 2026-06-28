# Round Order Animation & sq0 Bug Fix Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在每回合開始時加入「擲骰子決定出手順序」的視覺動畫，並修正第一回合因 sq0/sq1 座標重疊導致 NPC 在原地跳動的 bug。

**Architecture:** 純前端改動，不需要後端變更。出手順序直接從 `RoundExecutedPayload.actions` 的排列順序推導。`DiceRollPanelView` 在擲步數骰子之前先跑洗牌動畫；`RoundBottomHud` 按出手順序靜態排列槽位。sq0 bug 透過在 `BoardController` 為 sq0 指定一個獨立的 3D 座標解決。

**Tech Stack:** Unity 6 / DOTween / UniTask / C#

---

## Global Constraints

- 不修改後端任何程式碼
- 不新增後端 DTO 欄位
- 動畫參數（_shuffleSteps、_shuffleDuration、_settleDuration）必須是 `[SerializeField]`，方便 Inspector 調整
- 使用 DOTween (`DOAnchorPos`) 執行 RectTransform 動畫，與現有 DOTween 用法一致
- 所有 async 方法使用 UniTask + CancellationToken
- 順序來源：`payload.actions` 的排列即出手順序，`actions[0].npcId` 最先行動

---

## Files

### 修改
- `frontend/unity/Assets/Scripts/Race/BoardController.cs` — 新增 sq0 3D 座標，修正 GetSquarePosition clamp
- `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs` — 新增 `SlideToAsync`
- `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs` — 新增洗牌動畫，修改 `ShowAsync` 簽名
- `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs` — `SetRound` 加入 `npcOrder` 參數
- `frontend/unity/Assets/Scripts/Race/RacePresenter.cs` — 傳入 `npcOrder` 給 `ShowAsync` 和 `SetRound`

---

## Design

### Task 1：修正 sq0 Bug（BoardController）

**根因：** `GetSquarePosition(0)` 執行 `Mathf.Clamp(0, 1, 20)` → 回傳 `_positions[1]`。
因此 sq0 與 sq1 在 3D 世界中是同一座標，第一回合每個 NPC 的第一步（sq0 → sq1）是 DOJump in-place。

**修正：**

`BuildPositions()` 新增 sq0：
```csharp
_positions[0] = new Vector3(-_tileSpacing, 0f, 0f);
```
sq0 位於棋盤 sq1 左側一格距離的 off-board 起始區。

`GetSquarePosition` 下限改為 0：
```csharp
public Vector3 GetSquarePosition(int squareIndex)
{
    int clamped = Mathf.Clamp(squareIndex, 0, 20);
    return _positions[clamped];
}
```

**視覺效果：** NPC 出現在棋盤左側的起跑區，第一步真正往右移動到 sq1，不再原地跳。

---

### Task 2：DiceSlotView 新增 SlideToAsync

`DiceRollPanelView` 在洗牌動畫中需要平滑移動各張卡片的 `anchoredPosition`。

新增至 `DiceSlotView`：
```csharp
public UniTask SlideToAsync(Vector2 targetAnchoredPos, float duration, CancellationToken ct)
{
    return ((RectTransform)transform)
        .DOAnchorPos(targetAnchoredPos, duration)
        .SetEase(Ease.InOutSine)
        .ToUniTask(cancellationToken: ct);
}
```

---

### Task 3：DiceRollPanelView 洗牌動畫

**新 serialized fields：**
```csharp
[SerializeField] private int   _shuffleSteps    = 5;
[SerializeField] private float _shuffleDuration = 0.15f;
[SerializeField] private float _settleDuration  = 0.4f;
```

**ShowAsync 新簽名（加入 `npcOrder` 參數）：**
```csharp
public async UniTask ShowAsync(
    NpcConfig npcConfig,
    int[] npcOrder,
    Dictionary<int, int> npcDice,
    CancellationToken cancellationToken)
```

**新流程：**
1. `UIShowAsync` (scale in)
2. 為每個 slot 呼叫 `Setup(npcConfig.npcs[i])` 設定 NPC 資訊
3. `ShuffleOrderAsync(npcConfig, npcOrder, cancellationToken)` — 洗牌動畫
4. 擲步數骰子（現有邏輯，在洗牌後的位置執行）
5. `UIHideAsync` (scale out)

**ShuffleOrderAsync 演算法：**

```csharp
private async UniTask ShuffleOrderAsync(
    NpcConfig npcConfig, int[] npcOrder, CancellationToken ct)
{
    // 1. 讓 LayoutGroup 重新算好位置，讀取各 slot 的 anchoredPosition
    var layoutGroup = GetComponent<LayoutGroup>();
    if (layoutGroup != null) layoutGroup.enabled = true;
    LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);

    var panelPositions = new Vector2[_slots.Length];
    for (int i = 0; i < _slots.Length; i++)
        panelPositions[i] = ((RectTransform)_slots[i].transform).anchoredPosition;

    // 2. 關閉 LayoutGroup，改手動控制位置
    if (layoutGroup != null) layoutGroup.enabled = false;

    // 3. 建立 npcId → slotIndex 的對應
    var npcToSlot = new Dictionary<int, int>();
    for (int i = 0; i < npcConfig.npcs.Length; i++)
        npcToSlot[npcConfig.npcs[i].id] = i;

    // 4. 追蹤目前每個位置放的是哪個 slot
    var posToSlot = Enumerable.Range(0, _slots.Length).ToArray();
    var slotToPos = Enumerable.Range(0, _slots.Length).ToArray();

    // 5. 隨機 pair swap × _shuffleSteps
    var rng = new System.Random();
    for (int s = 0; s < _shuffleSteps; s++)
    {
        int p1 = rng.Next(_slots.Length);
        int p2 = (p1 + rng.Next(1, _slots.Length)) % _slots.Length;
        int slot1 = posToSlot[p1];
        int slot2 = posToSlot[p2];

        await UniTask.WhenAll(
            _slots[slot1].SlideToAsync(panelPositions[p2], _shuffleDuration, ct),
            _slots[slot2].SlideToAsync(panelPositions[p1], _shuffleDuration, ct));

        posToSlot[p1] = slot2; posToSlot[p2] = slot1;
        slotToPos[slot1] = p2; slotToPos[slot2] = p1;
    }

    // 6. 同時滑到最終目標位置並更新 sibling index
    var settleTasks = new UniTask[_slots.Length];
    for (int p = 0; p < npcOrder.Length && p < _slots.Length; p++)
    {
        if (!npcToSlot.TryGetValue(npcOrder[p], out int slotIdx)) continue;
        _slots[slotIdx].transform.SetSiblingIndex(p);
        settleTasks[slotIdx] = _slots[slotIdx].SlideToAsync(panelPositions[p], _settleDuration, ct);
    }
    await UniTask.WhenAll(settleTasks);
}
```

---

### Task 4：RoundBottomHud 按順序排列

**SetRound 新簽名（加入 `npcOrder` 參數）：**
```csharp
public void SetRound(int[] npcOrder, Dictionary<int, int> npcSteps)
{
    gameObject.SetActive(true);
    for (int i = 0; i < _slots.Length && i < npcOrder.Length; i++)
    {
        var entry = _npcConfig.GetById(npcOrder[i]);
        int? steps = npcSteps.TryGetValue(npcOrder[i], out int s) ? s : (int?)null;
        _slots[i].Setup(entry, steps);
        _slots[i].SetActive(false);
    }
}
```

`SetActiveNpc` 邏輯不變（用 npcId 比對）。

**視覺效果：** HUD 從左到右依序對應第一個出手、第二個出手……的 NPC；與 DiceRollPanel 洗牌後的順序一致。

---

### Task 5：RacePresenter 串接

在 `PlayRoundAsync` 中推導出手順序，並傳給兩個 UI：

```csharp
// 推導出手順序（payload.actions 的排列即出手順序）
var npcOrder = payload.actions.Select(a => a.npcId).ToArray();

// 傳入 npcOrder
await _dicePanel.ShowAsync(_npcConfig, npcOrder, npcDice, ct);
_bottomHud.SetRound(npcOrder, npcSteps);
```

其餘 `PlayRoundAsync` 邏輯不變。

---

## 動畫時序總覽（每回合）

```
[RoundToast: "Round N" 顯示/隱藏]
    → [DiceRollPanel scale in]
        → [洗牌動畫: ~5 × 0.15s = 0.75s]
        → [定位動畫: 0.4s]
        → [步數骰子 roll: ~1.2s]
        → [hold: 0.5s]
    → [DiceRollPanel scale out]
    → [BottomHud 出現（按出手順序排列）]
    → [NPC 移動動畫（逐一）]
    → [BottomHud 隱藏]
```
