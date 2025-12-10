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
    public TextMeshProUGUI timerText;
    public GameObject topicUIRoot;
    public GameObject votingUIRoot;

    public GameObject joinButton;
    public GameObject startButton;

    [Header("Game Settings")]
    public int werewolfCount = 1;
    public float announcementTime = 5.0f;
    public float buildTimeLimit = 20.0f;

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

    // ▼▼▼ これが不足していた変数です！ ▼▼▼
    [UdonSynced] public int currentGuesserId = -1;
    // ▲▲▲ ▲▲▲

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
        if (localPlayer.isMaster && Input.GetKeyDown(KeyCode.J))
        {
            DebugJoinAll();
        }

        if (isGameStarted && isBuildingPhase)
        {
            currentTimer -= Time.deltaTime;

            if (timerText != null)
            {
                float displayTime = Mathf.Max(0, currentTimer);
                timerText.text = $"Time: {displayTime:F1}";
            }

            if (localPlayer.isMaster && currentTimer <= 0)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnterThinkingPhase");
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
                for (int i = 0; i < playerCount; i++)
                {
                    if (playerIds[i] == p.playerId) joined = true;
                }

                if (!joined)
                {
                    playerIds[playerCount] = p.playerId;
                    playerCount++;
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

    public void OnClickStart()
    {
        if (!Networking.IsOwner(localPlayer, gameObject)) return;
        if (playerCount < 1) return;

        // 1. 役割抽選
        for (int i = 0; i < 20; i++) playerRoles[i] = 0;
        int assigned = 0;
        int safety = 0;
        while (assigned < werewolfCount && safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerRoles[rnd] == 0) { playerRoles[rnd] = 1; assigned++; }
            safety++;
        }

        // 2. 最初の親を決める
        int parentIndex = Random.Range(0, playerCount);
        currentParentId = playerIds[parentIndex];

        // 3. 最初の回答者を決める（親以外から選ぶ）
        PickNewGuesser();

        if (topicManager != null) topicManager.DrawNewTopics();

        isGameStarted = true;
        RequestSerialization();

        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }

    // 回答者を抽選する関数
    public void PickNewGuesser()
    {
        if (playerCount <= 1)
        {
            currentGuesserId = currentParentId;
            return;
        }

        int guesserIndex = -1;
        int safety = 0;

        while (safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerIds[rnd] != currentParentId)
            {
                guesserIndex = rnd;
                break;
            }
            safety++;
        }

        if (guesserIndex != -1)
        {
            currentGuesserId = playerIds[guesserIndex];
        }
    }

    public void StartGameSequence()
    {
        isBuildingPhase = false;
        isThinkingPhase = false;
        isProcessingResult = false;

        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";

        UpdateUI();
        SendCustomEventDelayedSeconds(nameof(EnterBuildingPhase), announcementTime);
    }

    public void EnterBuildingPhase()
    {
        isBuildingPhase = true;
        currentTimer = buildTimeLimit;

        if (bigRoleText != null) bigRoleText.text = "";
        if (topicUIRoot != null) topicUIRoot.SetActive(true);

        UpdateInventoryState();
    }

    public void EnterThinkingPhase()
    {
        isBuildingPhase = false;
        isThinkingPhase = true;

        if (inventoryManager != null) inventoryManager.SetActiveState(false);
        if (timerText != null) timerText.text = "Time's Up!";

        if (bigRoleText != null)
        {
            if (localPlayer.playerId == currentGuesserId)
            {
                bigRoleText.text = "<color=green>回答してください！</color>";
            }
            else
            {
                bigRoleText.text = "<color=yellow>シンキングタイム！</color>";
            }
        }

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

                if (!isBuildingPhase && !isThinkingPhase)
                {
                    ShowRoleText(amIParent);
                }
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
            string roleStr = "";
            if (myRoleID == 1)
            {
                roleStr = "<color=#FF0000>あなたは <size=150%>人狼</size> です</color>";
            }
            else
            {
                roleStr = "<color=#00FFFF>あなたは <size=150%>市民</size> です</color>";
            }

            if (amIParent)
            {
                roleStr += "\n<color=#FFFF00>あなたは [親] です！</color>";
            }

            if (localPlayer.playerId == currentGuesserId)
            {
                roleStr += "\n<color=#00FF00>次は [回答者] です！</color>";
            }

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

        if (isCorrect)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessCorrectAnswer");
        }
        else
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ProcessWrongAnswer");
        }
    }

    public void ProcessCorrectAnswer()
    {
        isProcessingResult = true;
        if (bigRoleText != null) bigRoleText.text = "<color=#00FFFF>正解！！</color>";

        if (Networking.IsOwner(gameObject))
        {
            citizenWins++;
            RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }
    }

    public void ProcessWrongAnswer()
    {
        isProcessingResult = true;
        if (bigRoleText != null) bigRoleText.text = "<color=#FF0000>不正解...</color>";

        if (Networking.IsOwner(gameObject))
        {
            werewolfWins++;
            RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }
    }

    public void StartNextTurn()
    {
        if (inventoryManager != null)
        {
            inventoryManager.SendCustomEvent("ClearAllBlocks");
        }

        if (topicManager != null) topicManager.DrawNewTopics();

        currentParentId = currentGuesserId;

        PickNewGuesser();

        RequestSerialization();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartGameSequence");
    }
}