using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public class GameFlowManager : UdonSharpBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI bigRoleText;   // 役職表示用（最初だけ出す）

    // ▼▼▼ 追加: ゲーム進行メッセージ用（シンキングタイム・正解不正解） ▼▼▼
    public TextMeshProUGUI phaseMessageText;
    // ▲▲▲ ▲▲▲

    public TextMeshProUGUI timerText;
    public TextMeshProUGUI scoreText;

    public GameObject topicUIRoot;
    public GameObject votingUIRoot;

    public GameObject joinButton;
    public GameObject startButton;

    [Header("Game Settings")]
    public int werewolfCount = 1;
    public float announcementTime = 5.0f;
    public float buildTimeLimit = 20.0f;
    public float thinkingTimeLimit = 20.0f;

    [Header("Managers")]
    public PlayerInventoryManager inventoryManager;
    public TopicManager topicManager;

    [Header("UI Roots")]
    public GameObject lobbyCanvasRoot;
    public GameObject gameUIRoot;

    [UdonSynced] public int[] playerIds = new int[20];
    [UdonSynced] public int playerCount = 0;
    [UdonSynced] public int[] playerRoles = new int[20];
    [UdonSynced] public int currentParentId = -1;
    [UdonSynced] public bool isGameStarted = false;
    [UdonSynced] public int currentGuesserId = -1;

    [Header("Score Data")]
    [UdonSynced] public int citizenWins = 0;
    [UdonSynced] public int werewolfWins = 0;

    private VRCPlayerApi localPlayer;

    [HideInInspector] public bool isBuildingPhase = false;
    [HideInInspector] public bool isThinkingPhase = false;

    private bool isProcessingResult = false;
    private float currentTimer = 0f;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        UpdateUI();
    }

    void Update()
    {
        if (localPlayer.isMaster && Input.GetKeyDown(KeyCode.J)) DebugJoinAll();

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

    public void DebugJoinAll()
    {
        Debug.Log("[Debug] Forcing all players to join...");
        VRCPlayerApi[] players = new VRCPlayerApi[20];
        VRCPlayerApi.GetPlayers(players);
        foreach (var p in players)
        {
            if (Utilities.IsValid(p))
            {
                bool joined = false;
                for (int i = 0; i < playerCount; i++) if (playerIds[i] == p.playerId) joined = true;
                if (!joined) { playerIds[playerCount] = p.playerId; playerCount++; }
            }
        }
        RequestSerialization();
        UpdateUI();
    }

    public override void OnDeserialization()
    {
        UpdateUI();
    }

    public void OnClickStart()
    {
        if (!Networking.IsOwner(localPlayer, gameObject)) return;
        if (playerCount < 1) return;

        citizenWins = 0;
        werewolfWins = 0;

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
        PickNewGuesser();

        if (topicManager != null) topicManager.DrawNewTopics();

        isGameStarted = true;
        RequestSerialization();

        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }

    public void PickNewGuesser()
    {
        if (playerCount <= 1) { currentGuesserId = currentParentId; return; }
        int guesserIndex = -1;
        int safety = 0;
        while (safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerIds[rnd] != currentParentId) { guesserIndex = rnd; break; }
            safety++;
        }
        if (guesserIndex != -1) currentGuesserId = playerIds[guesserIndex];
    }

    public void StartGameSequence()
    {
        isBuildingPhase = false;
        isThinkingPhase = false;
        isProcessingResult = false;

        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";

        // ▼▼▼ テキストリセット ▼▼▼
        if (phaseMessageText != null) phaseMessageText.text = "";
        // ▲▲▲ ▲▲▲

        UpdateUI(); // ここで「あなたは人狼です(BigRoleText)」が表示される
        SendCustomEventDelayedSeconds(nameof(EnterBuildingPhase), announcementTime);
    }

    public void EnterBuildingPhase()
    {
        isBuildingPhase = true;
        currentTimer = buildTimeLimit;

        // ▼▼▼ 役職表示を消す ▼▼▼
        if (bigRoleText != null) bigRoleText.text = "";
        // ▲▲▲ ▲▲▲

        if (topicUIRoot != null) topicUIRoot.SetActive(true);
        UpdateInventoryState();
    }

    public void EnterThinkingPhase()
    {
        isBuildingPhase = false;
        isThinkingPhase = true;
        currentTimer = thinkingTimeLimit;

        if (inventoryManager != null) inventoryManager.SetActiveState(false);

        // ▼▼▼ 進行メッセージを表示 (PhaseMessageText) ▼▼▼
        if (phaseMessageText != null)
        {
            if (localPlayer.playerId == currentGuesserId)
                phaseMessageText.text = "<color=green>回答してください！</color>";
            else
                phaseMessageText.text = "<color=yellow>シンキングタイム！</color>";
        }
        // ▲▲▲ ▲▲▲

        if (votingUIRoot != null) votingUIRoot.SetActive(true);
    }

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

            string guesserName = "Unknown";
            VRCPlayerApi guesserPlayer = VRCPlayerApi.GetPlayerById(currentGuesserId);
            if (Utilities.IsValid(guesserPlayer)) guesserName = guesserPlayer.displayName;

            if (statusText != null)
                statusText.text = $"Parent: {parentName}\nGuesser: {guesserName}";

            if (Utilities.IsValid(localPlayer))
            {
                bool amIParent = (localPlayer.playerId == currentParentId);
                if (topicManager != null) topicManager.HighlightAnswerForParent(amIParent);

                // 建築前だけ役職を出す
                if (!isBuildingPhase && !isThinkingPhase) ShowRoleText(amIParent);
                UpdateInventoryState();

                // ▼▼▼ スコア表示の修正（カラーコードの改行削除） ▼▼▼
                if (scoreText != null)
                {
                    if (amIParent)
                    {
                        scoreText.text = "";
                    }
                    else
                    {
                        // cyanのタグを繋げて記述
                        scoreText.text = $"<color=#00FFFF>市民: {citizenWins}勝</color> / <color=red>人狼: {werewolfWins}敗</color>";
                    }
                }
                // ▲▲▲ ▲▲▲
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
            string roleStr = "";
            if (myRoleID == 1) roleStr = "<color=#FF0000>あなたは <size=150%>人狼</size> です</color>";
            else roleStr = "<color=#00FFFF>あなたは <size=150%>市民</size> です</color>";

            if (amIParent) roleStr += "\n<color=#FFFF00>あなたは [親] です！</color>";
            if (localPlayer.playerId == currentGuesserId) roleStr += "\n<color=#00FF00>次は [回答者] です！</color>";

            bigRoleText.text = roleStr;
        }
    }

    private void UpdateInventoryState()
    {
        if (inventoryManager == null) return;
        bool amIParent = (localPlayer.playerId == currentParentId);
        inventoryManager.SetActiveState(isGameStarted && isBuildingPhase && amIParent);
    }

    public void OnAnswerResult(bool isCorrect)
    {
        if (isProcessingResult) return;
        isProcessingResult = true;

        if (isCorrect) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessCorrectAnswer");
        else SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessWrongAnswer");
    }

    public void ProcessTimeUp()
    {
        if (isProcessingResult) return;
        isProcessingResult = true;
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessWrongAnswer");
    }

    public void ProcessCorrectAnswer()
    {
        isProcessingResult = true;

        // ▼▼▼ メッセージ表示（PhaseMessageTextを使う） ▼▼▼
        if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF>正解！！</color>";

        if (Networking.IsOwner(gameObject))
        {
            citizenWins++;
            RequestSerialization();
            if (citizenWins >= 5) SendCustomEventDelayedSeconds(nameof(GameOverCitizen), 3.0f);
            else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }
    }

    public void ProcessWrongAnswer()
    {
        isProcessingResult = true;

        // ▼▼▼ メッセージ表示（PhaseMessageTextを使う） ▼▼▼
        if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000>不正解（または時間切れ）...</color>";

        if (Networking.IsOwner(gameObject))
        {
            werewolfWins++;
            RequestSerialization();
            if (werewolfWins >= 3) SendCustomEventDelayedSeconds(nameof(GameOverWerewolf), 3.0f);
            else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }
    }

    public void GameOverCitizen()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ShowCitizenWin");
    }
    public void GameOverWerewolf()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ShowWerewolfWin");
    }

    public void ShowCitizenWin()
    {
        // ▼▼▼ 勝敗表示（PhaseMessageTextを使う） ▼▼▼
        if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF><size=200%>市民チームの勝利！</size></color>";
        EndGameCleanup();
    }
    public void ShowWerewolfWin()
    {
        // ▼▼▼ 勝敗表示（PhaseMessageTextを使う） ▼▼▼
        if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000><size=200%>人狼チームの勝利！</size></color>";
        EndGameCleanup();
    }

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
        if (phaseMessageText != null) phaseMessageText.text = ""; // メッセージを消す
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