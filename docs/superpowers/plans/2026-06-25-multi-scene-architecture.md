# Multi-Scene Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將每個場景各自持有一份 ProjectScope 的架構重構為 Additive Multi-scene 模式，讓 ApiClient、SignalRClient、GameStateService、PlayerSession 整個遊戲生命週期中持久存活。

**Architecture:** 新增永久存活的 MainScene（含 Camera、EventSystem、ProjectScope）；LoginScene / LobbyScene / RaceScene 改為以 Additive 模式載入 / 卸載的子場景，透過 VContainer 的 `LifetimeScope.EnqueueParent` 在執行期動態綁定 parent scope。所有場景切換集中在新的 `SceneLoader` singleton 處理。

**Tech Stack:** Unity 6, VContainer 1.17 (`LifetimeScope.EnqueueParent`), UniTask, Unity MCP (`execute_code`)

## Global Constraints

- VContainer 1.17 — 使用 `LifetimeScope.EnqueueParent(scope)` 包裝 additive 載入，禁止用 `LifetimeScope.Find<T>()` 動態搜尋。
- 子場景 LifetimeScope 的 `_parent` Inspector 欄位一律留空；parent 由 `SceneLoader` 在執行期注入。
- 子場景不得包含 EventSystem（只能有 MainScene 的那一個）。
- LoginScene / LobbyScene 移除自己的 Camera；RaceScene 保留 3D Camera（position: (4.4, 14, −4)，rotation: (60, 0, 0)）。
- Build Settings 順序：MainScene(0)、LoginScene(1)、LobbyScene(2)、RaceScene(3)。
- `SceneLoader.LoadAsync` 不接受 CancellationToken 參數——場景切換一旦觸發就不可取消（避免子場景 MonoBehaviour 被銷毀時 token 提前取消導致新場景無法載入）。
- 所有 Presenter 的場景切換改呼叫 `_sceneLoader.LoadAsync(...).Forget()`。

---

### Task 1: SceneLoader、AppBootstrapper、Main.cs

**Files:**
- Create: `frontend/unity/Assets/Scripts/Core/SceneLoader.cs`
- Create: `frontend/unity/Assets/Scripts/Core/AppBootstrapper.cs`
- Modify: `frontend/unity/Assets/Scripts/Main.cs`

**Interfaces:**
- Produces:
  - `SceneLoader.LoadAsync(string sceneName): UniTask` — 卸載目前子場景並以 Additive 模式載入新場景
  - `AppBootstrapper` — MonoBehaviour，注入後在 `Start()` 呼叫 `SceneLoader.LoadAsync("LoginScene").Forget()`

---

- [ ] **Step 1: 建立 SceneLoader.cs**

路徑：`frontend/unity/Assets/Scripts/Core/SceneLoader.cs`

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace CubeRacing
{
    public class SceneLoader
    {
        private readonly LifetimeScope _mainScope;
        private UnityEngine.SceneManagement.Scene _currentSubScene;

        public SceneLoader(LifetimeScope mainScope) => _mainScope = mainScope;

        public async UniTask LoadAsync(string sceneName)
        {
            if (_currentSubScene.IsValid())
                await SceneManager.UnloadSceneAsync(_currentSubScene);

            using (LifetimeScope.EnqueueParent(_mainScope))
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            _currentSubScene = SceneManager.GetSceneByName(sceneName);
        }
    }
}
```

- [ ] **Step 2: 建立 AppBootstrapper.cs**

路徑：`frontend/unity/Assets/Scripts/Core/AppBootstrapper.cs`

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace CubeRacing
{
    public class AppBootstrapper : MonoBehaviour
    {
        private SceneLoader _sceneLoader;

        [Inject]
        public void Construct(SceneLoader sceneLoader) => _sceneLoader = sceneLoader;

        private void Start()
            => _sceneLoader.LoadAsync("LoginScene").Forget();
    }
}
```

- [ ] **Step 3: 修改 Main.cs — 新增三行注冊**

在 `Configure()` 方法末尾（`builder.RegisterInstance(_npcConfig);` 之後）新增：

```csharp
// Expose this LifetimeScope for SceneLoader injection
builder.RegisterInstance<LifetimeScope>(this);
builder.Register<SceneLoader>(Lifetime.Singleton);
builder.RegisterComponentInHierarchy<AppBootstrapper>();
```

完整 `Configure()` 最後幾行應為：

```csharp
            // Config
            builder.RegisterInstance(_npcConfig);

            // Scene management
            builder.RegisterInstance<LifetimeScope>(this);
            builder.Register<SceneLoader>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<AppBootstrapper>();
        }
```

- [ ] **Step 4: 等待 Unity 編譯完成並確認無錯誤**

在 Unity Editor 中：
1. 等待底部狀態欄編譯圖示消失
2. 開啟 Console 視窗（Window → General → Console）
3. 確認沒有紅色 error 訊息

預期：Console 無 compilation error。若有，根據錯誤訊息修正。

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Core/SceneLoader.cs \
        frontend/unity/Assets/Scripts/Core/AppBootstrapper.cs \
        frontend/unity/Assets/Scripts/Main.cs
git commit -m "feat: add SceneLoader singleton and AppBootstrapper for multi-scene support"
```

---

### Task 2: 更新 Presenter 的場景切換呼叫

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Login/LoginPresenter.cs`
- Modify: `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes: `SceneLoader.LoadAsync(string sceneName): UniTask`（Task 1 產出）

---

- [ ] **Step 1: 修改 LoginPresenter.cs**

變更摘要：
1. 新增欄位 `private SceneLoader _sceneLoader;`
2. `Construct()` 新增 `SceneLoader sceneLoader` 參數並賦值
3. 移除靜態方法 `LoadLobbyAsync`，改為在 `ContinueAsync` 和 `LoginAsync` 直接呼叫
4. 移除 `using UnityEngine.SceneManagement;`（不再直接使用）

完整修改後的 `LoginPresenter.cs`：

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class LoginPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _nicknameInput;
        [SerializeField] private Button         _loginButton;
        [SerializeField] private TMP_Text       _errorText;
        [SerializeField] private TMP_Text       _loginButtonText;

        private PlayerSession    _session;
        private ApiClient        _api;
        private GameStateService _gameState;
        private SceneLoader      _sceneLoader;

        [Inject]
        public void Construct(PlayerSession session, ApiClient api,
                              GameStateService gameState, SceneLoader sceneLoader)
        {
            _session     = session;
            _api         = api;
            _gameState   = gameState;
            _sceneLoader = sceneLoader;
        }

        private void Start()
        {
            _errorText.text = string.Empty;

            if (_session.HasSavedSession)
            {
                _nicknameInput.text   = _session.SavedNickname;
                _loginButtonText.text = $"Continue ({_session.SavedChips:N0} chips)";
                _loginButton.onClick.AddListener(() => ContinueAsync(destroyCancellationToken).Forget());
            }
            else
            {
                _loginButtonText.text = "Login";
                _loginButton.onClick.AddListener(() => LoginAsync(destroyCancellationToken).Forget());
            }
        }

        private async UniTaskVoid ContinueAsync(CancellationToken ct)
        {
            SetLoading(true);
            try
            {
                _session.LoadFromPrefs();
                _api.SetTokenProvider(() => _session.Token.ToString());
                _sceneLoader.LoadAsync("LobbyScene").Forget();
            }
            catch (Exception e)
            {
                _errorText.text = $"Load failed, please log in again: {e.Message}";
                PlayerPrefs.DeleteKey("player_token");
                _loginButtonText.text = "Login";
                _loginButton.onClick.RemoveAllListeners();
                _loginButton.onClick.AddListener(() => LoginAsync(destroyCancellationToken).Forget());
                SetLoading(false);
            }
        }

        private async UniTaskVoid LoginAsync(CancellationToken ct)
        {
            var nickname = _nicknameInput.text.Trim();
            if (string.IsNullOrEmpty(nickname))
            {
                _errorText.text = "Please enter a nickname";
                return;
            }

            SetLoading(true);
            try
            {
                var result = await _api.CreatePlayerAsync(nickname, ct);
                _session.Initialize(result.playerId, result.token, nickname, result.chipsBalance);
                _api.SetTokenProvider(() => _session.Token.ToString());
                _sceneLoader.LoadAsync("LobbyScene").Forget();
            }
            catch (ApiException ex)
            {
                _errorText.text = $"Login failed: {ex.ResponseBody}";
                SetLoading(false);
            }
        }

        private void SetLoading(bool loading)
        {
            _loginButton.interactable = !loading;
            if (loading)
                _loginButtonText.text = "Please wait...";
            else
                _loginButtonText.text = _session.HasSavedSession
                    ? $"Continue ({_session.SavedChips:N0} chips)"
                    : "Login";
        }
    }
}
```

注意：`SetLoading(false)` 已從 `finally` 移出——因為呼叫 `LoadAsync` 後場景會切換，MonoBehaviour 即將被銷毀，`finally` 中操作 UI 沒有意義且可能造成警告。

- [ ] **Step 2: 修改 LobbyPresenter.cs**

變更摘要：
1. 新增欄位 `private SceneLoader _sceneLoader;`
2. `Construct()` 新增 `SceneLoader sceneLoader` 參數並賦值
3. 401 catch 塊：`LoadSceneAsync("LoginScene")` → `_sceneLoader.LoadAsync("LoginScene").Forget()`
4. `OnWatchRaceClicked()`：`LoadSceneAsync("RaceScene")` → `_sceneLoader.LoadAsync("RaceScene").Forget()`
5. 移除 `using UnityEngine.SceneManagement;`

只需修改以下幾處（其他程式碼不動）：

```csharp
// 在欄位宣告區新增：
private SceneLoader _sceneLoader;

// Construct() 改為：
[Inject]
public void Construct(
    PlayerSession session, ApiClient api, SignalRClient signalR,
    GameStateService gameState, NpcConfig npcConfig,
    ISubscriber<SettlementDoneMessage> settlementSubscriber,
    SceneLoader sceneLoader)
{
    _session              = session;
    _api                  = api;
    _signalR              = signalR;
    _gameState            = gameState;
    _npcConfig            = npcConfig;
    _settlementSubscriber = settlementSubscriber;
    _sceneLoader          = sceneLoader;
}

// 401 catch 塊改為：
catch (ApiException ex) when (ex.StatusCode == 401)
{
    PlayerPrefs.DeleteKey("player_token");
    PlayerPrefs.DeleteKey("player_nickname");
    PlayerPrefs.DeleteKey("player_chips");
    PlayerPrefs.Save();
    _sceneLoader.LoadAsync("LoginScene").Forget();
    return;
}

// OnWatchRaceClicked() 改為：
private void OnWatchRaceClicked()
    => _sceneLoader.LoadAsync("RaceScene").Forget();
```

- [ ] **Step 3: 修改 RacePresenter.cs**

變更摘要：
1. 新增欄位 `private SceneLoader _sceneLoader;`
2. `Construct()` 新增 `SceneLoader sceneLoader` 參數並賦值
3. `ReturnToLobby()`：`LoadSceneAsync("LobbyScene")` → `_sceneLoader.LoadAsync("LobbyScene").Forget()`
4. 移除 `using UnityEngine.SceneManagement;`

只需修改以下幾處：

```csharp
// 欄位宣告區新增：
private SceneLoader _sceneLoader;

// Construct() 改為：
[Inject]
public void Construct(
    BoardController board, GameStateService gameState,
    PlayerSession session, NpcConfig npcConfig,
    ISubscriber<RoundExecutedMessage>  roundSubscriber,
    ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
    ISubscriber<SettlementDoneMessage> settlementSubscriber,
    SceneLoader sceneLoader)
{
    _board                   = board;
    _gameState               = gameState;
    _session                 = session;
    _npcConfig               = npcConfig;
    _roundSubscriber         = roundSubscriber;
    _raceCompletedSubscriber = raceCompletedSubscriber;
    _settlementSubscriber    = settlementSubscriber;
    _sceneLoader             = sceneLoader;
}

// ReturnToLobby() 改為：
private void ReturnToLobby()
    => _sceneLoader.LoadAsync("LobbyScene").Forget();
```

- [ ] **Step 4: 等待 Unity 編譯完成並確認無錯誤**

在 Unity Editor 中等待編譯完成，確認 Console 無紅色 error。

預期：無 compilation error。

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Login/LoginPresenter.cs \
        frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs \
        frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "refactor: replace direct SceneManager calls with SceneLoader in all presenters"
```

---

### Task 3: 在 Unity Editor 建立 MainScene

**Files:**
- Create: `frontend/unity/Assets/Scenes/MainScene.unity`

**Interfaces:**
- Produces: 含 Camera、EventSystem、[ProjectScope]（Main + NpcConfig）、[AppBootstrapper] 的 MainScene

**注意：** 此 Task 透過 Unity MCP `execute_code` 工具執行。`execute_code` 是 C# 6 CodeDom，不支援 local functions 和 `using` 宣告在方法體內；所有型別需使用完整命名空間。

---

- [ ] **Step 1: 建立 MainScene 基礎結構（Camera、EventSystem、ProjectScope、AppBootstrapper）**

透過 Unity MCP 的 `execute_code` 工具執行以下程式碼：

```csharp
// 建立並儲存 MainScene
var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);

// Main Camera
var cameraGO = new GameObject("Main Camera");
cameraGO.tag = "MainCamera";
var cam = cameraGO.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
cam.depth = -1;
cameraGO.AddComponent<AudioListener>();
var urpCamData = cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

// Directional Light
var lightGO = new GameObject("Directional Light");
var light = lightGO.AddComponent<Light>();
light.type = LightType.Directional;
lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
var urpLightData = lightGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();

// EventSystem
var esGO = new GameObject("EventSystem");
esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

// [ProjectScope]
var scopeGO = new GameObject("[ProjectScope]");
var mainScope = scopeGO.AddComponent<CubeRacing.Main>();
// Wire NpcConfig
var npcConfig = AssetDatabase.LoadAssetAtPath<CubeRacing.NpcConfig>("Assets/Scenes/Data/NpcConfig.asset");
if (npcConfig != null) {
    var so = new SerializedObject(mainScope);
    so.FindProperty("_npcConfig").objectReferenceValue = npcConfig;
    so.ApplyModifiedProperties();
}

// [AppBootstrapper]
var bootstrapGO = new GameObject("[AppBootstrapper]");
bootstrapGO.AddComponent<CubeRacing.AppBootstrapper>();

// 儲存場景
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(newScene, "Assets/Scenes/MainScene.unity");
return "MainScene created";
```

- [ ] **Step 2: 確認 MainScene 已正確建立**

使用 `execute_code` 驗證：

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
var result = $"Scene: {scene.name}, objects: {scene.rootCount}\n";
var objs = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
foreach (var go in objs) result += $"  {go.name}: {string.Join(", ", System.Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name))}\n";
return result;
```

預期輸出包含：`Main Camera`（含 Camera）、`EventSystem`（含 EventSystem）、`[ProjectScope]`（含 Main）、`[AppBootstrapper]`（含 AppBootstrapper）。

- [ ] **Step 3: 將 MainScene 加入 Build Settings 並設為 index 0**

```csharp
var scenes = new UnityEditor.EditorBuildSettingsScene[] {
    new UnityEditor.EditorBuildSettingsScene("Assets/Scenes/MainScene.unity", true),
    new UnityEditor.EditorBuildSettingsScene("Assets/Scenes/LoginScene.unity", true),
    new UnityEditor.EditorBuildSettingsScene("Assets/Scenes/LobbyScene.unity", true),
    new UnityEditor.EditorBuildSettingsScene("Assets/Scenes/RaceScene.unity", true),
};
UnityEditor.EditorBuildSettings.scenes = scenes;
AssetDatabase.SaveAssets();

var result = "Build Settings:\n";
for (int i = 0; i < UnityEditor.EditorBuildSettings.scenes.Length; i++) {
    var s = UnityEditor.EditorBuildSettings.scenes[i];
    result += $"  [{i}] {s.path} (enabled={s.enabled})\n";
}
return result;
```

預期：
```
[0] Assets/Scenes/MainScene.unity (enabled=True)
[1] Assets/Scenes/LoginScene.unity (enabled=True)
[2] Assets/Scenes/LobbyScene.unity (enabled=True)
[3] Assets/Scenes/RaceScene.unity (enabled=True)
```

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scenes/MainScene.unity \
        frontend/unity/Assets/Scenes/MainScene.unity.meta \
        frontend/unity/ProjectSettings/EditorBuildSettings.asset
git commit -m "feat: create MainScene with Camera, EventSystem, ProjectScope, AppBootstrapper"
```

---

### Task 4: 重構子場景（移除重複物件、清空 _parent）

**Files:**
- Modify: `frontend/unity/Assets/Scenes/LoginScene.unity`
- Modify: `frontend/unity/Assets/Scenes/LobbyScene.unity`
- Modify: `frontend/unity/Assets/Scenes/RaceScene.unity`

**Interfaces:**
- Consumes: 已存在的三個子場景；已存在的 MainScene（Task 3 產出）

---

- [ ] **Step 1: 重構 LoginScene（移除 Camera、EventSystem，清空 LoginScope._parent）**

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/LoginScene.unity");

// 刪除 Main Camera
var cam = GameObject.Find("Main Camera");
if (cam != null) Object.DestroyImmediate(cam);

// 刪除 EventSystem
var es = GameObject.Find("EventSystem");
if (es != null) Object.DestroyImmediate(es);

// 清空 LoginScope 的 _parent
var loginScope = UnityEngine.Object.FindFirstObjectByType<CubeRacing.LoginScope>();
if (loginScope != null) {
    var so = new SerializedObject(loginScope);
    so.FindProperty("_parent").objectReferenceValue = null;
    so.ApplyModifiedProperties();
}

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "LoginScene refactored";
```

- [ ] **Step 2: 重構 LobbyScene（移除 Camera、EventSystem，清空 LobbyScope._parent）**

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");

var cam = GameObject.Find("Main Camera");
if (cam != null) Object.DestroyImmediate(cam);

var es = GameObject.Find("EventSystem");
if (es != null) Object.DestroyImmediate(es);

var lobbyScope = UnityEngine.Object.FindFirstObjectByType<CubeRacing.LobbyScope>();
if (lobbyScope != null) {
    var so = new SerializedObject(lobbyScope);
    so.FindProperty("_parent").objectReferenceValue = null;
    so.ApplyModifiedProperties();
}

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "LobbyScene refactored";
```

- [ ] **Step 3: 重構 RaceScene（移除 EventSystem，清空 RaceScope._parent；保留 Camera）**

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/RaceScene.unity");

// 只移除 EventSystem，保留 Main Camera（RaceScene 需要自己的 3D Camera）
var es = GameObject.Find("EventSystem");
if (es != null) Object.DestroyImmediate(es);

var raceScope = UnityEngine.Object.FindFirstObjectByType<CubeRacing.RaceScope>();
if (raceScope != null) {
    var so = new SerializedObject(raceScope);
    so.FindProperty("_parent").objectReferenceValue = null;
    so.ApplyModifiedProperties();
}

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "RaceScene refactored";
```

- [ ] **Step 4: 驗證——確認子場景不含 EventSystem 且 _parent 為空**

```csharp
var result = "";
var scenePaths = new[] {
    "Assets/Scenes/LoginScene.unity",
    "Assets/Scenes/LobbyScene.unity",
    "Assets/Scenes/RaceScene.unity"
};
foreach (var path in scenePaths) {
    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
    var hasCamera  = GameObject.Find("Main Camera") != null;
    var hasES      = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null;
    var scope      = UnityEngine.Object.FindFirstObjectByType<VContainer.Unity.LifetimeScope>();
    var parentNull = true;
    if (scope != null) {
        var so = new SerializedObject(scope);
        parentNull = so.FindProperty("_parent").objectReferenceValue == null;
    }
    result += $"{scene.name}: camera={hasCamera}, eventSystem={hasES}, parentNull={parentNull}\n";
}
return result;
```

預期：
```
LoginScene: camera=False, eventSystem=False, parentNull=True
LobbyScene: camera=False, eventSystem=False, parentNull=True
RaceScene:  camera=True,  eventSystem=False, parentNull=True
```

- [ ] **Step 5: 開啟 MainScene 並進入 Play Mode 確認完整流程**

1. 在 Unity Editor 開啟 `Assets/Scenes/MainScene.unity`
2. 確認 Hierarchy 中有：`Main Camera`、`Directional Light`、`EventSystem`、`[ProjectScope]`、`[AppBootstrapper]`
3. 點擊 **Play**
4. 觀察 Console：應看到 LoginScene 被 additive 載入（無 error）
5. 輸入暱稱並點擊 Login → 應切換到 LobbyScene（LoginScene 卸載、LobbyScene additive 載入）
6. 整個流程中 Console 不應出現：
   - `VContainer Exception`
   - `NullReferenceException`
   - `EventSystem: There can only be one active EventSystem at once` 警告

- [ ] **Step 6: Commit**

```bash
git add frontend/unity/Assets/Scenes/LoginScene.unity \
        frontend/unity/Assets/Scenes/LobbyScene.unity \
        frontend/unity/Assets/Scenes/RaceScene.unity \
        frontend/unity/ProjectSettings/EditorBuildSettings.asset
git commit -m "refactor: convert sub-scenes to additive mode — remove duplicate Camera/EventSystem, clear parent refs"
```
