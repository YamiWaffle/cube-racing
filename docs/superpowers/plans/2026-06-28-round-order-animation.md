# Round Order Animation & sq0 Bug Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正第一回合 sq0/sq1 座標重疊 bug，並加入每回合「擲骰子決定出手順序」洗牌動畫，同時讓 `RoundBottomHud` 按出手順序排列槽位。

**Architecture:** 純前端改動，不需要後端變更。出手順序從 `RoundExecutedPayload.actions` 的排列順序推導（`actions[0].npcId` 最先行動）。BoardController 新增 sq0 的獨立 3D 座標解決 in-place jump bug；DiceRollPanelView 在擲步數骰子前跑洗牌動畫（DOAnchorPos 滑動卡片 + 最終定位）；RacePresenter 提取 `npcOrder` 並傳給兩個 UI 組件。

**Tech Stack:** Unity 6 / C# / DOTween (DOAnchorPos) / UniTask / UnityEngine.UI (LayoutGroup / LayoutRebuilder)

## Global Constraints

- 不修改後端任何程式碼，不新增後端 DTO 欄位
- 動畫參數 `_shuffleSteps`、`_shuffleDuration`、`_settleDuration` 必須是 `[SerializeField]`
- 所有 async 方法使用 UniTask + CancellationToken，與現有慣例一致
- 使用 DOTween `DOAnchorPos` 執行 RectTransform 動畫
- 出手順序來源：`payload.actions.Select(a => a.npcId).ToArray()`（`actions[0].npcId` 最先行動）

---

## File Structure

| 檔案 | 變動類型 | 責任 |
|------|---------|------|
| `frontend/unity/Assets/Scripts/Race/BoardController.cs` | 修改 | 新增 sq0 的 off-board 3D 座標，修正 GetSquarePosition clamp |
| `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs` | 修改 | 新增 `SlideToAsync`（DOAnchorPos 滑動動畫） |
| `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs` | 修改 | 洗牌動畫 `ShuffleOrderAsync`，修改 `ShowAsync` 簽名加入 `npcOrder` |
| `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs` | 修改 | `SetRound` 加入 `npcOrder` 參數，按順序填充 slots |
| `frontend/unity/Assets/Scripts/Race/RacePresenter.cs` | 修改 | 提取 `npcOrder`，傳給 `_dicePanel.ShowAsync` 和 `_bottomHud.SetRound` |

---

### Task 1：BoardController — 修正 sq0 Bug

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/BoardController.cs`

**Interfaces:**
- Produces: `GetSquarePosition(0)` 回傳 `new Vector3(-_tileSpacing, 0f, 0f)`（sq1 左側一格）

**背景：** `_positions` 是 `Vector3[21]`（index 0–20）。目前 `BuildPositions()` 只建立 index 1–20，`GetSquarePosition` 執行 `Mathf.Clamp(0, 1, 20)` 讓 sq0 和 sq1 共用相同座標，造成第一回合 NPC 在原地 DOJump。

- [ ] **Step 1：修改 `BuildPositions()`，在迴圈之前新增 sq0 座標**

  ```csharp
  private void BuildPositions()
  {
      float s    = _tileSpacing;
      _positions[0] = new Vector3(-s, 0f, 0f); // off-board 起始區，sq1 左側
      int   cols = 5;
  
      for (int i = 1; i <= 20; i++)
      {
          int   row = (i - 1) / cols;
          int   col = (i - 1) % cols;
          float x   = (row % 2 == 0) ? col * s : (cols - 1 - col) * s;
          float z   = row * s;
          _positions[i] = new Vector3(x, 0f, z);
      }
  }
  ```

- [ ] **Step 2：修改 `GetSquarePosition()`，下限改為 0**

  ```csharp
  public Vector3 GetSquarePosition(int squareIndex)
  {
      int clamped = Mathf.Clamp(squareIndex, 0, 20);
      return _positions[clamped];
  }
  ```

- [ ] **Step 3：進入 Unity Editor Play Mode，觀察 NPC 起始位置**

  預期：NPC 出現在棋盤 sq1 左側約 2.2 units 的位置（off-board），而非直接在 sq1 上。第一回合 NPC 的第一步應為從 off-board 區真正往右移動到 sq1，不再「原地跳」。在 Unity Console 中確認無 Error。

- [ ] **Step 4：Commit**

  ```bash
  git add frontend/unity/Assets/Scripts/Race/BoardController.cs
  git commit -m "fix: give sq0 a distinct off-board 3D position to fix round-1 in-place jump"
  ```

---

### Task 2：DiceSlotView + DiceRollPanelView — 洗牌動畫

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs`

**Interfaces:**
- Consumes: `DiceSlotView.SlideToAsync(Vector2, float, CancellationToken) : UniTask`（Task 2 自己新增）
- Consumes: `UIBehaviour.RectTransform`（已存在於基底類別）
- Produces: `DiceRollPanelView.ShowAsync(NpcConfig, int[], Dictionary<int,int>, CancellationToken) : UniTask`（Task 4 呼叫此簽名）

**背景：**
- `DiceSlotView` 繼承 `MonoBehaviour`，有 `Setup`、`SetEmpty`、`RollAsync` 三個方法。需新增 `SlideToAsync`。
- `DiceRollPanelView` 繼承 `UIBehaviour`（有 `RectTransform` 屬性），現有 `ShowAsync(NpcConfig, Dictionary<int,int>, CancellationToken)`。需改簽名並加入 `ShuffleOrderAsync`。
- `DiceRollPanel` GameObject 上有 `HorizontalLayoutGroup`，負責排列 `DiceSlotView` 子物件。洗牌動畫期間需暫停 LayoutGroup 以手動控制位置。

- [ ] **Step 1：在 `DiceSlotView.cs` 新增 `SlideToAsync` 方法**

  在 `RollAsync` 方法之後新增：

  ```csharp
  public UniTask SlideToAsync(Vector2 targetAnchoredPos, float duration, CancellationToken ct)
  {
      return ((RectTransform)transform)
          .DOAnchorPos(targetAnchoredPos, duration)
          .SetEase(Ease.InOutSine)
          .ToUniTask(cancellationToken: ct);
  }
  ```

  確認 `DiceSlotView.cs` 的 using directives 包含 `DG.Tweening` 和 `Cysharp.Threading.Tasks`（現有 `RollAsync` 已有）。

  完整 `DiceSlotView.cs`：

  ```csharp
  using System.Threading;
  using Cysharp.Threading.Tasks;
  using DG.Tweening;
  using TMPro;
  using UnityEngine;
  using UnityEngine.UI;
  
  namespace CubeRacing
  {
      public class DiceSlotView : MonoBehaviour
      {
          [SerializeField] private Image    _colorBlock;
          [SerializeField] private TMP_Text _nameText;
          [SerializeField] private TMP_Text _numberText;
          [SerializeField] private float    _rollIntervalSec = 0.08f;
  
          public void Setup(NpcEntry entry)
          {
              _colorBlock.color = entry.color;
              _nameText.text    = entry.npcName;
              _numberText.text  = "—";
          }
  
          public void SetEmpty()
          {
              _numberText.text = "—";
          }
  
          public async UniTask RollAsync(int finalValue, float rollDuration, CancellationToken ct)
          {
              float elapsed = 0f;
              while (elapsed < rollDuration && !ct.IsCancellationRequested)
              {
                  _numberText.text = Random.Range(1, 4).ToString();
                  await UniTask.Delay((int)(_rollIntervalSec * 1000), cancellationToken: ct);
                  elapsed += _rollIntervalSec;
              }
              _numberText.text = finalValue.ToString();
          }
  
          public UniTask SlideToAsync(Vector2 targetAnchoredPos, float duration, CancellationToken ct)
          {
              return ((RectTransform)transform)
                  .DOAnchorPos(targetAnchoredPos, duration)
                  .SetEase(Ease.InOutSine)
                  .ToUniTask(cancellationToken: ct);
          }
      }
  }
  ```

- [ ] **Step 2：用完整新版本取代 `DiceRollPanelView.cs`**

  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using Cysharp.Threading.Tasks;
  using UnityEngine;
  using UnityEngine.UI;
  
  namespace CubeRacing
  {
      public class DiceRollPanelView : UIBehaviour
      {
          [SerializeField] private DiceSlotView[] _slots;
          [SerializeField] private float          _rollDuration    = 1.2f;
          [SerializeField] private float          _holdDuration    = 0.5f;
          [SerializeField] private int            _shuffleSteps    = 5;
          [SerializeField] private float          _shuffleDuration = 0.15f;
          [SerializeField] private float          _settleDuration  = 0.4f;
  
          public async UniTask ShowAsync(
              NpcConfig npcConfig,
              int[] npcOrder,
              Dictionary<int, int> npcDice,
              CancellationToken cancellationToken)
          {
              await UIShowAsync(cancellationToken: cancellationToken);
  
              for (int i = 0; i < _slots.Length; i++)
                  _slots[i].Setup(npcConfig.npcs[i]);
  
              await ShuffleOrderAsync(npcConfig, npcOrder, cancellationToken);
  
              var tasks = new UniTask[_slots.Length];
              for (int i = 0; i < _slots.Length; i++)
              {
                  var entry = npcConfig.npcs[i];
                  if (npcDice.TryGetValue(entry.id, out int dice))
                      tasks[i] = _slots[i].RollAsync(dice, _rollDuration, cancellationToken);
                  else
                  {
                      _slots[i].SetEmpty();
                      tasks[i] = UniTask.CompletedTask;
                  }
              }
  
              await UniTask.WhenAll(tasks);
              await UniTask.Delay((int)(_holdDuration * 1000), cancellationToken: cancellationToken);
              await UIHideAsync(cancellationToken: cancellationToken);
          }
  
          private async UniTask ShuffleOrderAsync(
              NpcConfig npcConfig, int[] npcOrder, CancellationToken ct)
          {
              // 讓 LayoutGroup 算好初始位置後讀取，再暫停讓我們手動控制
              var layoutGroup = GetComponent<LayoutGroup>();
              if (layoutGroup != null) layoutGroup.enabled = true;
              LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);
  
              var panelPositions = new Vector2[_slots.Length];
              for (int i = 0; i < _slots.Length; i++)
                  panelPositions[i] = ((RectTransform)_slots[i].transform).anchoredPosition;
  
              if (layoutGroup != null) layoutGroup.enabled = false;
  
              // npcId → slot index（_slots[i] 對應 npcConfig.npcs[i]）
              var npcToSlot = new Dictionary<int, int>();
              for (int i = 0; i < npcConfig.npcs.Length; i++)
                  npcToSlot[npcConfig.npcs[i].id] = i;
  
              // 追蹤目前每個位置放的 slot
              var posToSlot = Enumerable.Range(0, _slots.Length).ToArray();
              var slotToPos = Enumerable.Range(0, _slots.Length).ToArray();
  
              // 隨機 pair swap × _shuffleSteps
              var rng = new System.Random();
              for (int s = 0; s < _shuffleSteps; s++)
              {
                  int p1    = rng.Next(_slots.Length);
                  int p2    = (p1 + rng.Next(1, _slots.Length)) % _slots.Length;
                  int slot1 = posToSlot[p1];
                  int slot2 = posToSlot[p2];
  
                  await UniTask.WhenAll(
                      _slots[slot1].SlideToAsync(panelPositions[p2], _shuffleDuration, ct),
                      _slots[slot2].SlideToAsync(panelPositions[p1], _shuffleDuration, ct));
  
                  posToSlot[p1] = slot2; posToSlot[p2] = slot1;
                  slotToPos[slot1] = p2; slotToPos[slot2] = p1;
              }
  
              // 所有 slot 同時滑到最終目標位置
              var settleTasks = new UniTask[_slots.Length];
              for (int p = 0; p < npcOrder.Length && p < _slots.Length; p++)
              {
                  if (!npcToSlot.TryGetValue(npcOrder[p], out int slotIdx)) continue;
                  _slots[slotIdx].transform.SetSiblingIndex(p);
                  settleTasks[slotIdx] =
                      _slots[slotIdx].SlideToAsync(panelPositions[p], _settleDuration, ct);
              }
              await UniTask.WhenAll(settleTasks);
          }
      }
  }
  ```

- [ ] **Step 3：確認 Unity Console 無 Compile Error**

  在 Unity Editor 等待編譯完成，確認 Console 無 Error。

- [ ] **Step 4：暫時在 `RacePresenter.PlayRoundAsync` 呼叫 `_dicePanel.ShowAsync` 的地方加入臨時的 `npcOrder`（讓程式可以 build），等 Task 4 完整整合**

  這一步只是讓專案維持可編譯狀態。在 `PlayRoundAsync` 中找到：
  ```csharp
  await _dicePanel.ShowAsync(_npcConfig, npcDice, ct);
  ```
  暫時改為（Task 5 會正式替換）：
  ```csharp
  var tempOrder = payload.actions.Select(a => a.npcId).ToArray();
  await _dicePanel.ShowAsync(_npcConfig, tempOrder, npcDice, ct);
  ```
  同時在 `RacePresenter.cs` 的 using directives 加入 `using System.Linq;`。

- [ ] **Step 5：進入 Play Mode 觀察洗牌動畫**

  預期：`DiceRollPanel` scale in 後，卡片快速隨機左右互換位置（約 5 次，每次 0.15s），最後所有卡片同時滑到最終順序位置（0.4s），再執行現有的擲步數骰子動畫，最後 scale out。Console 無 Error。

- [ ] **Step 6：Commit**

  ```bash
  git add frontend/unity/Assets/Scripts/Race/DiceSlotView.cs \
          frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs \
          frontend/unity/Assets/Scripts/Race/RacePresenter.cs
  git commit -m "feat: add shuffle order animation to DiceRollPanelView"
  ```

---

### Task 3：RoundBottomHud — 按出手順序排列

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs`

**Interfaces:**
- Consumes: `NpcConfig.GetById(int id) : NpcEntry`（已存在；`npcs.FirstOrDefault(n => n.id == id)`，不存在時回傳 null）
- Produces: `RoundBottomHud.SetRound(int[] npcOrder, Dictionary<int, int> npcSteps)`（Task 4 呼叫此簽名）

**背景：** 目前 `SetRound(Dictionary<int, int> npcSteps)` 按 `_npcConfig.npcs[i]` 原始順序填充 slots；`SetActiveNpc` 用 `_npcConfig.npcs[i].id == npcId` 比對。新版：`SetRound` 按 `npcOrder` 填充（`_slots[0]` = 第一個出手的 NPC），並將 `npcOrder` 儲存到 `_npcOrder` 欄位；`SetActiveNpc` 改為比對 `_npcOrder[i] == npcId`，確保高亮的 slot 與實際移動的 NPC 對應。

- [ ] **Step 1：用完整新版本取代 `RoundBottomHud.cs`**

  ```csharp
  using System.Collections.Generic;
  using UnityEngine;
  
  namespace CubeRacing
  {
      public class RoundBottomHud : MonoBehaviour
      {
          [SerializeField] private RoundHudSlotView[] _slots;
  
          private NpcConfig _npcConfig;
          private int[]     _npcOrder;
  
          public void Initialize(NpcConfig npcConfig)
          {
              _npcConfig = npcConfig;
          }
  
          public void SetRound(int[] npcOrder, Dictionary<int, int> npcSteps)
          {
              _npcOrder = npcOrder;
              gameObject.SetActive(true);
              for (int i = 0; i < _slots.Length && i < npcOrder.Length; i++)
              {
                  var entry = _npcConfig.GetById(npcOrder[i]);
                  if (entry == null) continue;
                  int? steps = npcSteps.TryGetValue(npcOrder[i], out int s) ? s : (int?)null;
                  _slots[i].Setup(entry, steps);
                  _slots[i].SetActive(false);
              }
          }
  
          public void SetActiveNpc(int npcId)
          {
              if (_npcOrder == null) return;
              for (int i = 0; i < _slots.Length && i < _npcOrder.Length; i++)
                  _slots[i].SetActive(_npcOrder[i] == npcId);
          }
  
          public void Hide() => gameObject.SetActive(false);
      }
  }
  ```

- [ ] **Step 2：確認 Unity Console 無 Compile Error**

- [ ] **Step 3：Commit**

  ```bash
  git add frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs
  git commit -m "feat: RoundBottomHud orders slots by turn order"
  ```

---

### Task 4：RacePresenter — 串接 npcOrder

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes: `DiceRollPanelView.ShowAsync(NpcConfig, int[], Dictionary<int,int>, CancellationToken)`（Task 2 產出）
- Consumes: `RoundBottomHud.SetRound(int[], Dictionary<int,int>)`（Task 3 產出）

**背景：** `PlayRoundAsync` 需要在提取 `npcSteps`/`npcDice` 的同時提取 `npcOrder`，並傳給兩個 UI。Task 2 已加了暫時的 `tempOrder`，這裡做正式整合並移除 `tempOrder`。

- [ ] **Step 1：修改 `PlayRoundAsync` 中的相關程式碼**

  找到 `RacePresenter.cs` 的 `PlayRoundAsync` 方法。完整修改後的方法開頭到 HUD 呼叫處如下：

  ```csharp
  private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
  {
      if (payload.actions == null || payload.actions.Count == 0) return;

      var npcOrder = payload.actions.Select(a => a.npcId).ToArray();
      var npcSteps = new Dictionary<int, int>();
      var npcDice  = new Dictionary<int, int>();
      foreach (var action in payload.actions)
      {
          int steps = action.toSquare - action.fromSquare;
          npcSteps[action.npcId] = steps;
          npcDice[action.npcId]  = action.diceRoll;
          foreach (var carried in action.carriedNpcIds)
          {
              npcSteps[carried] = steps;
              npcDice[carried]  = action.diceRoll;
          }
      }

      await _roundToast.ShowAsync(payload.roundNumber, ct);
      await _dicePanel.ShowAsync(_npcConfig, npcOrder, npcDice, ct);
      _bottomHud.SetRound(npcOrder, npcSteps);

      // 以下的 foreach (var action in payload.actions) 及後續邏輯不變
      ...
  }
  ```

  確認 `using System.Linq;` 已存在（Task 2 Step 4 應該已加）。若未加，在 using directives 加入。

- [ ] **Step 2：確認 Unity Console 無 Compile Error**

- [ ] **Step 3：進入 Play Mode，跑完整流程**

  預期行為（每回合）：
  1. RoundToast 顯示「Round N」
  2. DiceRollPanel scale in → 卡片隨機洗牌 5 次 → 滑到出手順序位置 → 擲步數骰子 → scale out
  3. BottomHud 出現，槽位從左到右對應出手順序
  4. NPC 依序移動，當前移動的 NPC 對應槽位高亮
  5. BottomHud 隱藏

  第一回合特別確認：NPC 從棋盤左側 off-board 位置開始移動，無 in-place jump。

  Console 無 Error。

- [ ] **Step 4：Commit**

  ```bash
  git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs
  git commit -m "feat: integrate npcOrder into PlayRoundAsync for dice panel and bottom HUD"
  ```
