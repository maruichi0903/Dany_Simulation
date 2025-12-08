using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public class GameFlowManager : UdonSharpBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI bigRoleText; // 役割表示（最初はこれだけ出す）

    // ▼▼▼ 新規追加: タイマーとお題のUI ▼▼▼
    public TextMeshProUGUI timerText;   // タイマー表示用
    public GameObject topicUIRoot;      // お題が表示されているCanvasや親オブジェクト
    // ▲▲▲ ▲▲▲

    public GameObject joinButton;
    public GameObject startButton;

    [Header("Game Settings")]
    public int werewolfCount = 1;
    public float announcementTime = 5.0f; // 役割表示の時間
    public float buildTimeLimit = 20.0f;  // 建築制限時間（秒）

    [Header("Managers")]
    public PlayerInventoryManager inventoryManager;
    public TopicManager topicManager;

    [Header("UI Roots")]
    public GameObject lobbyCanvasRoot;
    public GameObject gameUIRoot;

    // 同期変数
    [UdonSynced] public int[] playerIds = new int[20];
    [UdonSynced] public int playerCount = 0;
    [UdonSynced] public int[] playerRoles = new int[20];
    [UdonSynced] public int currentParentId = -1;
    [UdonSynced] public bool isGameStarted = false;

    private VRCPlayerApi localPlayer;

    // フェーズ管理フラグ
    private bool isBuildingPhase = false; // 建築中か？
    private bool isThinkingPhase = false; // シンキングタイムか？

    // タイマー管理
    private float currentTimer = 0f;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        UpdateUI();
    }

    void Update()
    {
        // Masterのみ実行可能
        if (localPlayer.isMaster && Input.GetKeyDown(KeyCode.J))
        {
            DebugJoinAll();
        }

        // 建築フェーズ中のみタイマーを動かす
        if (isGameStarted && isBuildingPhase)
        {
            // 時間を減らす
            currentTimer -= Time.deltaTime;

            // 画面表示更新 ( "Time: 15.4" のように表示 )
            if (timerText != null)
            {
                // 0以下にはしない
                float displayTime = Mathf.Max(0, currentTimer);
                timerText.text = $"Time: {displayTime:F1}";
            }

            // タイマー終了判定 (Masterのみが監視して全員に号令を出す)
            if (localPlayer.isMaster && currentTimer <= 0)
            {
                // 全員をシンキングタイムへ移行させる
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnterThinkingPhase");
            }
        }
    }

    public void DebugJoinAll()
    {
        Debug.Log("[Debug] Forcing all players to join...");

        // ワールドにいる全プレイヤーを取得
        VRCPlayerApi[] players = new VRCPlayerApi[20]; // 最大人数分確保
        VRCPlayerApi.GetPlayers(players);

        foreach (var p in players)
        {
            if (Utilities.IsValid(p))
            {
                // まだリストにいなければ追加
                bool joined = false;
                for (int i = 0; i < playerCount; i++)
                {
                    if (playerIds[i] == p.playerId) joined = true;
                }

                if (!joined)
                {
                    playerIds[playerCount] = p.playerId;
                    playerCount++;
                    Debug.Log("Added player: " + p.displayName);
                }
            }
        }

        RequestSerialization();
        UpdateUI();
    }

    public override void OnDeserialization()
    {
        UpdateUI();
    }

    // --- ゲーム進行シーケンス ---

    public void OnClickStart()
    {
        if (!Networking.IsOwner(localPlayer, gameObject)) return;
        if (playerCount < 1) return;

        // 1. 抽選処理 (省略なしで記述)
        for (int i = 0; i < 20; i++) playerRoles[i] = 0;
        int assigned = 0;
        int safety = 0;
        while (assigned < werewolfCount && safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerRoles[rnd] == 0) { playerRoles[rnd] = 1; assigned++; }
            safety++;
        }
        int parentIndex = Random.Range(0, playerCount);
        currentParentId = playerIds[parentIndex];

        // 2. お題抽選
        if (topicManager != null) topicManager.DrawNewTopics();

        // 3. ゲーム開始
        isGameStarted = true;
        RequestSerialization();

        // 4. シーケンス開始
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }

    public void StartGameSequence()
    {
        // フェーズ初期化
        isBuildingPhase = false;
        isThinkingPhase = false;

        // ★ここがポイント: 開始直後はお題(topicUIRoot)を隠す！
        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";

        UpdateUI(); // 役割を表示

        // 5秒後に建築フェーズへ
        SendCustomEventDelayedSeconds(nameof(EnterBuildingPhase), announcementTime);
    }

    public void EnterBuildingPhase()
    {
        // 建築フェーズ開始
        isBuildingPhase = true;

        // タイマーセット
        currentTimer = buildTimeLimit;

        // 役割表示を消す
        if (bigRoleText != null) bigRoleText.text = "";

        // ★ここで初めて「お題」を表示する！
        if (topicUIRoot != null) topicUIRoot.SetActive(true);

        UpdateInventoryState();
    }

    // ▼▼▼ 新規追加: シンキングタイムへの移行 ▼▼▼
    public void EnterThinkingPhase()
    {
        // 建築終了
        isBuildingPhase = false;
        isThinkingPhase = true;

        // インベントリ強制OFF (親も操作できなくなる)
        if (inventoryManager != null) inventoryManager.SetActiveState(false);

        // タイマー表示固定
        if (timerText != null) timerText.text = "Time's Up!";

        // ここで「シンキングタイム！」などの文字を出しても良い
        if (bigRoleText != null) bigRoleText.text = "<color=yellow>シンキングタイム！</color>";
    }
    // ▲▲▲ ▲▲▲

    // --- その他 (JoinGameなどは変更なし) ---
    public void JoinGame()
    {
        if (localPlayer == null || isGameStarted) return;
        bool joined = false;
        for (int i = 0; i < playerCount; i++) if (playerIds[i] == localPlayer.playerId) joined = true;
        if (joined) return;
        Networking.SetOwner(localPlayer, gameObject);
        playerIds[playerCount] = localPlayer.playerId;
        playerCount++;
        RequestSerialization();
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(!isGameStarted);
        if (gameUIRoot != null) gameUIRoot.SetActive(isGameStarted);

        if (!isGameStarted)
        {
            if (statusText != null) statusText.text = "Waiting... (" + playerCount + " Joined)";
            if (inventoryManager != null) inventoryManager.SetActiveState(false);
        }
        else
        {
            string parentName = "Unknown";
            VRCPlayerApi parentPlayer = VRCPlayerApi.GetPlayerById(currentParentId);
            if (Utilities.IsValid(parentPlayer)) parentName = parentPlayer.displayName;
            if (statusText != null) statusText.text = "Parent is " + parentName;

            if (Utilities.IsValid(localPlayer))
            {
                // 親かどうか
                bool amIParent = (localPlayer.playerId == currentParentId);

                // お題の正解表示 (TopicManager)
                if (topicManager != null) topicManager.HighlightAnswerForParent(amIParent);

                // 役割表示 (建築前だけ出す)
                if (!isBuildingPhase && !isThinkingPhase)
                {
                    ShowRoleText(amIParent);
                }

                // インベントリ状態更新 (建築中のみ)
                UpdateInventoryState();
            }
        }
    }

    private void ShowRoleText(bool amIParent)
    {
        int myRoleID = -1;
        for (int i = 0; i < playerCount; i++)
        {
            if (playerIds[i] == localPlayer.playerId) { myRoleID = playerRoles[i]; break; }
        }

        if (bigRoleText != null)
        {
            string roleStr = (myRoleID == 1) ? "<color=red>あなたは 人狼 です</color>" : "<color=cyan>あなたは 市民 です</color>";
            if (amIParent) roleStr += "\n<color=yellow>あなたは [親] です！</color>";
            bigRoleText.text = roleStr;
        }
    }

    private void UpdateInventoryState()
    {
        if (inventoryManager == null) return;
        bool amIParent = (localPlayer.playerId == currentParentId);

        // 建築フェーズ かつ 親 のときだけON
        inventoryManager.SetActiveState(isGameStarted && isBuildingPhase && amIParent);
    }
}