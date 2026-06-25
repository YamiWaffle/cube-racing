# Multi-Scene Architecture Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 將目前每個場景各自持有一份 `ProjectScope` 的架構，重構為 Additive Multi-scene 模式，讓單例服務（`ApiClient`、`SignalRClient`、`GameStateService`、`PlayerSession`）在整個遊戲生命週期中持久存活，不隨場景切換而重建。

**Architecture:** 新增一個永遠存活的 `MainScene`，持有 `EventSystem`、主 `Camera`、以及 `ProjectScope`（`Main.cs`）。`LoginScene`、`LobbyScene`、`RaceScene` 改為以 Additive 模式載入 / 卸載的子場景，各自的 `LifetimeScope` 透過 VContainer 的 `EnqueueParent` API 在載入時動態綁定到 `MainScene` 的 `ProjectScope`。

**Tech Stack:** Unity 6, VContainer 1.17, UniTask, `SceneManager.LoadSceneAsync` (Additive), `LifetimeScope.EnqueueParent`

---

## Global Constraints

- VContainer 版本 1.17 — 使用 `LifetimeScope.EnqueueParent(scope)` 包裝 additive 載入，不使用 `Find<T>()` 動態搜尋。
- 子場景 LifetimeScope 的 `_parent` 欄位（Inspector）一律留空；parent 由 `SceneLoader` 在執行期注入。
- 場景切換期間不能有短暫的「兩個 EventSystem 並存」狀況——子場景不得包含 EventSystem。
- `LoginScene`、`LobbyScene` 移除自己的 Camera（UI 是 Screen Space Overlay，不需要）；`RaceScene` 保留自己的 3D Camera（position: (4.4, 14, −4)，rotation: (60, 0, 0)）。
- Build Settings 順序：MainScene(0)、LoginScene(1)、LobbyScene(2)、RaceScene(3)。
- 第一個載入的場景是 `MainScene`，它在 `Start()` 中 additive 載入 `LoginScene`。

---

## Scene Structure

### MainScene（永久存活）

| GameObject | Components |
|---|---|
| `Main Camera` | Camera (clear flags: Solid Color, depth: -1), AudioListener |
| `EventSystem` | EventSystem, StandaloneInputModule |
| `[ProjectScope]` | `Main`（LifetimeScope）+ `NpcConfig` 賦值 |
| `[AppBootstrapper]` | `AppBootstrapper`（MonoBehaviour）— 初始載入 LoginScene |

### LoginScene（Additive 子場景）

移除：`Main Camera`、`EventSystem`。  
保留：`Canvas`、`[LoginScope]`、`[LoginPresenter]`。

### LobbyScene（Additive 子場景）

移除：`Main Camera`、`EventSystem`。  
保留：`Canvas`、`[LobbyScope]`、`[LobbyPresenter]` 及相關 UI。

### RaceScene（Additive 子場景）

移除：`EventSystem`。  
保留：自己的 `Main Camera`（3D top-down）、`Board`、`Canvas`、`[RaceScope]`、`[RacePresenter]`。

---

## New Components

### `AppBootstrapper.cs`

`MainScene` 專用的 MonoBehaviour，負責遊戲啟動後載入第一個子場景：

```csharp
public class AppBootstrapper : MonoBehaviour
{
    private SceneLoader _sceneLoader;

    [Inject]
    public void Construct(SceneLoader sceneLoader) => _sceneLoader = sceneLoader;

    private void Start()
        => _sceneLoader.LoadAsync("LoginScene", destroyCancellationToken).Forget();
}
```

### `SceneLoader.cs`

Singleton，注冊在 `ProjectScope`。集中管理所有場景切換：

```csharp
public class SceneLoader
{
    private readonly LifetimeScope _mainScope;
    private Scene _currentSubScene;

    public SceneLoader(LifetimeScope mainScope) => _mainScope = mainScope;

    public async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        if (_currentSubScene.IsValid())
            await SceneManager.UnloadSceneAsync(_currentSubScene).ToUniTask(ct);

        using (LifetimeScope.EnqueueParent(_mainScope))
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive)
                              .ToUniTask(ct);

        _currentSubScene = SceneManager.GetSceneByName(sceneName);
    }
}
```

`Main.cs` 需新增：

```csharp
builder.RegisterComponentInHierarchy<AppBootstrapper>();
builder.Register<SceneLoader>(Lifetime.Singleton);
```

`SceneLoader` 的建構子需要 `LifetimeScope` 本身，VContainer 透過 `builder.RegisterInstance(this)` 注入（`this` 是 `Main` 實例）。

---

## Scene Transition Changes

各 Presenter 中所有 `SceneManager.LoadSceneAsync(...)` 呼叫，一律改為：

```csharp
await _sceneLoader.LoadAsync("TargetScene", ct);
```

需修改的呼叫位置：

| 檔案 | 原呼叫 | 改後 |
|---|---|---|
| `LoginPresenter.cs:100` | `LoadSceneAsync("LobbyScene")` | `_sceneLoader.LoadAsync("LobbyScene", ct)` |
| `LoginPresenter.cs:88` | `LoadSceneAsync("LoginScene")` | `_sceneLoader.LoadAsync("LoginScene", ct)` |
| `LobbyPresenter.cs:168` | `LoadSceneAsync("RaceScene")` | `_sceneLoader.LoadAsync("RaceScene", ct)` |
| `RacePresenter.cs:166` | `LoadSceneAsync("LobbyScene")` | `_sceneLoader.LoadAsync("LobbyScene", ct)` |

各 Presenter 的 `Construct()` 需新增 `SceneLoader _sceneLoader` 參數（VContainer 自動注入）。

---

## Sub-scope Parent Wiring

`LoginScope`、`LobbyScope`、`RaceScope` 的 `_parent` Inspector 欄位**清空**。Parent 在 `SceneLoader.LoadAsync` 執行期間透過 `EnqueueParent` 提供，子場景 Awake 時 VContainer 自動取用。子場景 scope 腳本本身無需任何修改。

---

## Build Settings

```
Index 0 — MainScene
Index 1 — LoginScene
Index 2 — LobbyScene
Index 3 — RaceScene
```

Unity Editor 中需在 Build Settings 新增 `MainScene` 並設為第 0 位。

---

## What Stays / What Changes

| | 現況 | 重構後 |
|---|---|---|
| ProjectScope 生命週期 | 每次切景重建 | 整個遊戲存活 |
| SignalR 連線 | 每次切景斷開重連 | 持久存活 |
| PlayerSession / GameStateService | 每次切景重建 | 持久存活 |
| Sub-scope parent | Inspector `_parent` 欄位 | `EnqueueParent` 動態注入 |
| Camera 數量 | 每個場景各一 | Main 1 個 + RaceScene 自有 1 個 |
| EventSystem 數量 | 每個場景各一 | MainScene 唯一 1 個 |
| 場景切換 API | `LoadSceneAsync(name)` Single | `SceneLoader.LoadAsync` Additive |
| Build Settings 數量 | 3 個場景 | 4 個場景（Main 在 index 0）|

---

## Error Handling

- `SceneLoader.LoadAsync` 執行中若 `CancellationToken` 取消，`UnloadSceneAsync` 或 `LoadSceneAsync` 的 UniTask 會丟出 `OperationCanceledException`，由呼叫端（Presenter）的 `try/catch` 自行處理，與現況一致。
- 若 `LoadAsync` 在 unload 完成前被再次呼叫（理論上不應發生，因 UI 按鈕在轉景期間應被禁用），`_currentSubScene.IsValid()` 仍能安全執行，不會 crash。
