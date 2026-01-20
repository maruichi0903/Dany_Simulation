using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public class GameFlowManager : UdonSharpBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI bigRoleText;
    public TextMeshProUGUI phaseMessageText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI scoreText;

    public GameObject topicUIRoot;
    public GameObject votingUIRoot;

    // joinButtonは不要になりますが、エラー防止のため残して
    public GameObject joinButton;
    public GameObject startButton;

    public Transform gameSpawnPoint;

    [Header("Game Settings")]
    public int werewolfCount = 1;
    public float announcementTime = 5.0f;
    public float buildTimeLimit = 45.0f;
    public float thinkingTimeLimit = 20.0f;

    [Header("Managers")]
    public PlayerInventoryManager inventoryManager;
    public TopicManager topicManager;

    [Header("UI Roots")]
    public GameObject lobbyCanvasRoot;
    public GameObject gameUIRoot;

    // ▼▼▼ 同期変数の管理 ▼▼▼
    [UdonSynced] public int[] playerIds = new int[20];
    [UdonSynced] public int playerCount = 0;
    [UdonSynced] public int[] playerRoles = new int[20];
    [UdonSynced] public int currentParentId = -1;
    [UdonSynced] public bool isGameStarted = false;
    [UdonSynced] public int currentGuesserId = -1;

    [Header("Score Data")]
    [UdonSynced] public int citizenWins = 0;
    [UdonSynced] public int werewolfWins = 0;

    [Header("Game State")]
    [UdonSynced] public bool isBuildingPhase = false; // [UdonSynced]を追加
    [UdonSynced] public bool isThinkingPhase = false; // [UdonSynced]を追加

    private VRCPlayerApi localPlayer;

    private bool isProcessingResult = false;
    private float currentTimer = 0f;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (votingUIRoot != null) votingUIRoot.SetActive(false);

        // ★自分がマスター（最初の1人）なら、自分をリストに登録
        if (Networking.IsOwner(gameObject))
        {
            RegisterPlayer(localPlayer);
        }

        UpdateUI();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (Networking.IsOwner(gameObject))
        {
            RegisterPlayer(player);
            SendCustomEventDelayedSeconds(nameof(_ForceSync), 2.0f);
        }
    }

    public void _ForceSync()
    {
        if (Networking.IsOwner(gameObject)) RequestSerialization();
    }

    // ★誰かがワールドから抜けたら呼ばれる（自動削除）
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (Networking.IsOwner(gameObject))
        {
            RemovePlayer(player);
        }
    }

    // プレイヤー登録処理（内部用）
    private void RegisterPlayer(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;
        if (isGameStarted) return; // ゲーム中なら入れない

        // 既にリストにいないかチェック
        for (int i = 0; i < playerCount; i++)
        {
            if (playerIds[i] == player.playerId) return; // 既にいる
        }

        if (playerCount < playerIds.Length)
        {
            playerIds[playerCount] = player.playerId;
            playerCount++;
            RequestSerialization(); // 全員に同期！
            UpdateUI();
        }
    }

    // プレイヤー削除処理（内部用）
    private void RemovePlayer(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        int removeIndex = -1;
        for (int i = 0; i < playerCount; i++)
        {
            if (playerIds[i] == player.playerId)
            {
                removeIndex = i;
                break;
            }
        }

        if (removeIndex != -1)
        {
            // 穴埋め処理（最後の人を、空いた穴に持ってくる）
            // ※順番が変わりますが、ロビー段階なら問題ありません
            if (removeIndex != playerCount - 1)
            {
                playerIds[removeIndex] = playerIds[playerCount - 1];
            }
            playerIds[playerCount - 1] = 0;
            playerCount--;

            RequestSerialization(); // 全員に同期！
            UpdateUI();
        }
    }

    void Update()
    {
        // デバッグ用（Jキー参加は不要になるので削除してもOK）
        // if (localPlayer.isMaster && Input.GetKeyDown(KeyCode.J)) DebugJoinAll();

        if (isGameStarted)
        {
            if (isBuildingPhase || isThinkingPhase)
            {
                currentTimer -= Time.deltaTime;
                if (timerText != null)
                {
                    float displayTime = Mathf.Max(0, currentTimer);
                    timerText.text = $"Time: {displayTime:F1}";
                }

                if (localPlayer.isMaster && currentTimer <= 0)
                {
                    if (isBuildingPhase)
                    {
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnterThinkingPhase");
                    }
                    else if (isThinkingPhase)
                    {
                        if (!isProcessingResult) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessTimeUp");
                    }
                }
            }
        }
    }

    public override void OnDeserialization()
    {
        UpdateUI();
    }

    // ★スタートボタンが押された時の処理
    public void OnClickStart()
    {
        // 誰が押してもOK。「オーナー（マスター）さん、始めてください！」とお願いを送る
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, "TryStartGame");
    }

    // ★オーナーだけが実行する開始処理
    public void TryStartGame()
    {
        if (playerCount < 1) return; // 1人以上いないとダメ

        citizenWins = 0;
        werewolfWins = 0;

        for (int i = 0; i < 20; i++) playerRoles[i] = 0;
        int assigned = 0;
        int safety = 0;

        // 人狼を決める
        while (assigned < werewolfCount && safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerRoles[rnd] == 0) { playerRoles[rnd] = 1; assigned++; }
            safety++;
        }

        // 親（Parent）を決める
        int parentIndex = Random.Range(0, playerCount);
        currentParentId = playerIds[parentIndex];
        PickNewGuesser();

        if (topicManager != null) topicManager.DrawNewTopics();

        isGameStarted = true;
        RequestSerialization(); // 「ゲーム始まったよ」情報を全員に送信

        // 全員一斉にゲーム開始シーケンスへ
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }

    // （JoinGame関数は削除しました）

    public void PickNewGuesser() { if (playerCount <= 1) { currentGuesserId = currentParentId; return; } int guesserIndex = -1; int safety = 0; while (safety < 100) { int rnd = Random.Range(0, playerCount); if (playerIds[rnd] != currentParentId) { guesserIndex = rnd; break; } safety++; } if (guesserIndex != -1) currentGuesserId = playerIds[guesserIndex]; }

    public void StartGameSequence()
    {
        isBuildingPhase = false;
        isThinkingPhase = false;
        isProcessingResult = false;

        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";
        if (phaseMessageText != null) phaseMessageText.text = "";

        // 全員ワープ！
        if (localPlayer != null && gameSpawnPoint != null)
        {
            localPlayer.TeleportTo(gameSpawnPoint.position, gameSpawnPoint.rotation);
        }

        UpdateUI();
        SendCustomEventDelayedSeconds(nameof(EnterBuildingPhase), announcementTime);
    }

    public void EnterBuildingPhase()
    {
        isBuildingPhase = true;
        currentTimer = buildTimeLimit;

        if (bigRoleText != null) bigRoleText.text = "";
        if (topicUIRoot != null) topicUIRoot.SetActive(true);

        if (localPlayer.playerId == currentParentId && inventoryManager != null)
        {
            inventoryManager.RefillInventory();
        }

        UpdateInventoryState();
    }
    public void EnterThinkingPhase()
    {
        isBuildingPhase = false;
        isThinkingPhase = true;
        currentTimer = thinkingTimeLimit;

        Debug.Log("[GameFlow] EnterThinkingPhase called. Activating voting UI."); // ログ追加

        if (inventoryManager != null) inventoryManager.SetActiveState(false);

        if (phaseMessageText != null)
        {
            if (localPlayer.playerId == currentGuesserId)
                phaseMessageText.text = "<color=green>回答してください</color>";
            else
                phaseMessageText.text = "<color=yellow>シンキングタイム</color>";
        }

        // ここでUIを表示
        if (votingUIRoot != null) votingUIRoot.SetActive(true);
    }

    public void UpdateUI()
    {
        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(!isGameStarted);
        if (gameUIRoot != null) gameUIRoot.SetActive(isGameStarted);

        if (!isGameStarted)
        {
            // ロビー画面の更新
            if (statusText != null) statusText.text = "Waiting... (" + playerCount + " Joined)";
            if (inventoryManager != null) inventoryManager.SetActiveState(false);
        }
        else
        {
            // ゲーム中画面の更新
            string parentName = "Unknown";
            VRCPlayerApi parentPlayer = VRCPlayerApi.GetPlayerById(currentParentId);
            if (Utilities.IsValid(parentPlayer)) parentName = parentPlayer.displayName;

            string guesserName = "Unknown";
            VRCPlayerApi guesserPlayer = VRCPlayerApi.GetPlayerById(currentGuesserId);
            if (Utilities.IsValid(guesserPlayer)) guesserName = guesserPlayer.displayName;

            if (statusText != null) statusText.text = $"Parent: {parentName}\nGuesser: {guesserName}";

            if (Utilities.IsValid(localPlayer))
            {
                bool amIParent = (localPlayer.playerId == currentParentId);
                if (topicManager != null) topicManager.HighlightAnswerForParent(amIParent);
                if (!isBuildingPhase && !isThinkingPhase) ShowRoleText(amIParent);
                UpdateInventoryState();
                if (scoreText != null)
                {
                    if (amIParent) { scoreText.text = ""; }
                    else { scoreText.text = $"<color=#00FFFF>市民: {citizenWins}勝</color> / <color=#FF0000>人狼: {werewolfWins}勝</color>"; }
                }
            }
        }
    }

    private void ShowRoleText(bool amIParent) { int myRoleID = -1; for (int i = 0; i < playerCount; i++) { if (playerIds[i] == localPlayer.playerId) { myRoleID = playerRoles[i]; break; } } if (bigRoleText != null) { string roleStr = ""; if (myRoleID == 1) roleStr = "<color=#FF0000>あなたは <size=150%>人狼</size> です</color>"; else roleStr = "<color=#00FFFF>あなたは <size=150%>市民</size> です</color>"; if (amIParent) roleStr += "\n<color=#FFFF00>あなたは [親] です</color>"; if (localPlayer.playerId == currentGuesserId) roleStr += "\n<color=#00FF00>次は [回答者] です</color>"; bigRoleText.text = roleStr; } }
    private void UpdateInventoryState() { if (inventoryManager == null) return; bool amIParent = (localPlayer.playerId == currentParentId); inventoryManager.SetActiveState(isGameStarted && isBuildingPhase && amIParent); }
    public void OnAnswerResult(bool isCorrect) { if (isProcessingResult) return; isProcessingResult = true; if (isCorrect) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessCorrectAnswer"); else SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessWrongAnswer"); }
    public void ProcessTimeUp() { if (isProcessingResult) return; isProcessingResult = true; SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessWrongAnswer"); }
    public void ProcessCorrectAnswer() { isProcessingResult = true; if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF>正解！！</color>"; if (Networking.IsOwner(gameObject)) { citizenWins++; RequestSerialization(); if (citizenWins >= 5) SendCustomEventDelayedSeconds(nameof(GameOverCitizen), 3.0f); else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f); } }
    public void ProcessWrongAnswer() { isProcessingResult = true; if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000>不正解（または時間切れ）</color>"; if (Networking.IsOwner(gameObject)) { werewolfWins++; RequestSerialization(); if (werewolfWins >= 3) SendCustomEventDelayedSeconds(nameof(GameOverWerewolf), 3.0f); else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f); } }
    public void GameOverCitizen() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ShowCitizenWin"); }
    public void GameOverWerewolf() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ShowWerewolfWin"); }
    public void ShowCitizenWin() { if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF><size=200%>市民チームの勝利！</size></color>"; EndGameCleanup(); }
    public void ShowWerewolfWin() { if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000><size=200%>人狼チームの勝利！</size></color>"; EndGameCleanup(); }

    public void EndGameCleanup()
    {
        isGameStarted = false;
        isBuildingPhase = false;
        isThinkingPhase = false;
        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";
        if (inventoryManager != null) inventoryManager.SendCustomEvent("ClearAllBlocks");
        SendCustomEventDelayedSeconds(nameof(ReturnToLobby), 5.0f);
    }

    public void ReturnToLobby()
    {
        if (phaseMessageText != null) phaseMessageText.text = "";

        if (localPlayer != null)
        {
            localPlayer.TeleportTo(new Vector3(0, -6.0f, 0), Quaternion.identity);
        }
        UpdateUI();
    }

    public void StartNextTurn()
    {
        if (inventoryManager != null) inventoryManager.SendCustomEvent("ClearAllBlocks");
        if (topicManager != null) topicManager.DrawNewTopics();
        currentParentId = currentGuesserId;
        PickNewGuesser();
        RequestSerialization();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }
}