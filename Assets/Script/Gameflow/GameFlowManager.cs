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

    [Header("Teleport Settings")]
    public Transform votingTeleportPoint;

    [UdonSynced] public int[] playerIds = new int[20];
    [UdonSynced] public int playerCount = 0;
    [UdonSynced] public int[] playerRoles = new int[20];
    [UdonSynced] public int currentParentId = -1;
    [UdonSynced] public int currentGuesserId = -1;

    [UdonSynced] public bool isGameStarted = false;
    [UdonSynced] public bool isBuildingPhase = false;
    [UdonSynced] public bool isThinkingPhase = false;
    [UdonSynced] public bool isProcessingResult = false;

    [UdonSynced] public int citizenWins = 0;
    [UdonSynced] public int werewolfWins = 0;
    [UdonSynced] private double phaseEndTime;

    private VRCPlayerApi localPlayer;
    private float currentTimer = 0f;
    private bool lastIsBuildingPhase = false;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer)) return;

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
            // スマホ等のロード遅延対策で、1秒後にも同期を要求
            SendCustomEventDelayedSeconds(nameof(_ForceSync), 1.0f);
        }
    }

    public void _ForceSync() { if (Networking.IsOwner(gameObject)) RequestSerialization(); }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (Networking.IsOwner(gameObject)) RemovePlayer(player);
    }

    private void RegisterPlayer(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || isGameStarted) return;
        for (int i = 0; i < playerCount; i++) { if (playerIds[i] == player.playerId) return; }
        if (playerCount < playerIds.Length)
        {
            playerIds[playerCount] = player.playerId;
            playerCount++;
            RequestSerialization();
            UpdateUI();
        }
    }

    private void RemovePlayer(VRCPlayerApi player)
    {
        int removeIndex = -1;
        for (int i = 0; i < playerCount; i++) { if (playerIds[i] == player.playerId) { removeIndex = i; break; } }
        if (removeIndex != -1)
        {
            if (removeIndex != playerCount - 1) playerIds[removeIndex] = playerIds[playerCount - 1];
            playerIds[playerCount - 1] = 0;
            playerCount--;
            RequestSerialization();
            UpdateUI();
        }
    }

    void Update()
    {
        if (isGameStarted)
        {
            if (isBuildingPhase || isThinkingPhase)
            {
                currentTimer = (float)(phaseEndTime - Networking.GetServerTimeInSeconds());

                if (timerText != null)
                {
                    float displayTime = Mathf.Max(0, currentTimer);
                    timerText.text = $"Time: {displayTime:F1}";
                }

                // タイマー終了判定はオーナー（マスター）だけが行う
                if (Networking.IsOwner(gameObject) && currentTimer <= 0)
                {
                    if (isBuildingPhase)
                    {
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(EnterThinkingPhase));
                    }
                    else if (isThinkingPhase && !isProcessingResult)
                    {
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ProcessTimeUp));
                    }
                }
            }
        }
    }

    public override void OnDeserialization()
    {
        if (isBuildingPhase && !lastIsBuildingPhase)
        {
            if (localPlayer.playerId == currentParentId && inventoryManager != null)
            {
                inventoryManager.RefillInventory();
            }
        }
        lastIsBuildingPhase = isBuildingPhase;

        UpdateUI();
    }

    public void OnClickStart() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(TryStartGame)); }

    public void TryStartGame()
    {
        if (!Networking.IsOwner(gameObject) || playerCount < 1) return;

        citizenWins = 0;
        werewolfWins = 0;
        for (int i = 0; i < 20; i++) playerRoles[i] = 0;

        // 人狼を1人決める
        int werewolfIdx = Random.Range(0, playerCount);
        playerRoles[werewolfIdx] = 1;

        // 親を決める
        int parentIdx = Random.Range(0, playerCount);
        currentParentId = playerIds[parentIdx];
        PickNewGuesser();

        if (topicManager != null) topicManager.DrawNewTopics();

        isGameStarted = true;
        RequestSerialization();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(StartGameSequence));
    }

    public void PickNewGuesser()
    {
        if (playerCount <= 1) { currentGuesserId = currentParentId; return; }
        int guesserIdx = -1;
        int safety = 0;
        while (safety < 100)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerIds[rnd] != currentParentId) { guesserIdx = rnd; break; }
            safety++;
        }
        if (guesserIdx != -1) currentGuesserId = playerIds[guesserIdx];
    }

    public void StartGameSequence()
    {
        if (Networking.IsOwner(gameObject))
        {
            isBuildingPhase = false;
            isThinkingPhase = false;
            isProcessingResult = false;
            RequestSerialization();
        }

        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";
        if (phaseMessageText != null) phaseMessageText.text = "";

        if (localPlayer != null && gameSpawnPoint != null)
            localPlayer.TeleportTo(gameSpawnPoint.position, gameSpawnPoint.rotation);

        UpdateUI();
        if (Networking.IsOwner(gameObject))
        {
            SendCustomEventDelayedSeconds(nameof(EnterBuildingPhase), announcementTime);
        }
    }

    public void _NetEnterBuildingPhase()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(EnterBuildingPhase));
    }

    public void EnterBuildingPhase()
    {
        if (Networking.IsOwner(gameObject))
        {
            isBuildingPhase = true;
            isThinkingPhase = false;
            phaseEndTime = Networking.GetServerTimeInSeconds() + buildTimeLimit;
            RequestSerialization();
        }
        if (localPlayer.playerId == currentParentId && inventoryManager != null)
            inventoryManager.RefillInventory();

        UpdateUI();
    }

    public void EnterThinkingPhase()
    {
        if (Networking.IsOwner(gameObject))
        {
            isBuildingPhase = false;
            isThinkingPhase = true;
            phaseEndTime = Networking.GetServerTimeInSeconds() + thinkingTimeLimit;
            RequestSerialization();
        }
        if (inventoryManager != null) inventoryManager.SetActiveState(false);

        // 回答者の強制移動
        if (localPlayer.playerId == currentGuesserId && votingTeleportPoint != null)
            localPlayer.TeleportTo(votingTeleportPoint.position, votingTeleportPoint.rotation);

        if (phaseMessageText != null)
        {
            if (localPlayer.playerId == currentGuesserId) phaseMessageText.text = "<color=green>回答してください</color>";
            else phaseMessageText.text = "<color=yellow>シンキングタイム</color>";
        }
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
            if (timerText != null) timerText.text = "";
        }
        else
        {
            bool showBoard = (isBuildingPhase || isThinkingPhase) && !isProcessingResult;
            if (topicUIRoot != null) topicUIRoot.SetActive(showBoard);
            if (votingUIRoot != null) votingUIRoot.SetActive(isThinkingPhase && !isProcessingResult);

            if (topicManager != null)
            {
                bool shouldHighlight = (localPlayer.playerId == currentParentId) && isBuildingPhase && !isProcessingResult;
                topicManager._ApplyTopicHighlight(shouldHighlight);
            }

            if (timerText != null)
            {
                if (!showBoard) timerText.text = "";
            }

            if (!isBuildingPhase && !isThinkingPhase && !isProcessingResult)
            {
                ShowRoleText(Networking.LocalPlayer.playerId == currentParentId);
            }
            else
            {
                if (bigRoleText != null) bigRoleText.text = "";
            }

            if (!isProcessingResult)
            {
                if (isThinkingPhase)
                {
                    if (localPlayer.playerId == currentGuesserId)
                        phaseMessageText.text = "<color=green>回答してください</color>";
                    else
                        phaseMessageText.text = "<color=yellow>シンキングタイム</color>";
                }
                else
                {
                    phaseMessageText.text = ""; // 結果演出が終わったら消去
                }
            }

            if (scoreText != null)
            {
                scoreText.text = $"<color=#00FFFF>市民: {citizenWins}勝</color> / <color=#FF0000>人狼: {werewolfWins}勝</color>";
            }

            UpdatePlayerNames();
            UpdateInventoryState();
        }
    }

    // プレイヤー名更新部分をスッキリさせるための補助メソッド
    private void UpdatePlayerNames()
    {
        string parentName = "Unknown";
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(currentParentId);
        if (Utilities.IsValid(p)) parentName = p.displayName;

        string guesserName = "Unknown";
        VRCPlayerApi g = VRCPlayerApi.GetPlayerById(currentGuesserId);
        if (Utilities.IsValid(g)) guesserName = g.displayName;

        if (statusText != null) statusText.text = $"Parent: {parentName}\nGuesser: {guesserName}";
    }

    private void ShowRoleText(bool amIParent)
    {
        int myRoleID = -1;
        for (int i = 0; i < playerCount; i++) { if (playerIds[i] == localPlayer.playerId) { myRoleID = playerRoles[i]; break; } }
        if (bigRoleText != null)
        {
            string roleStr = (myRoleID == 1) ? "<color=#FF0000>あなたは <size=150%>人狼</size> です</color>" : "<color=#00FFFF>あなたは <size=150%>市民</size> です</color>";
            if (amIParent) roleStr += "\n<color=#FFFF00>あなたは [親] です</color>";
            if (localPlayer.playerId == currentGuesserId) roleStr += "\n<color=#00FF00>次は [回答者] です</color>";
            bigRoleText.text = roleStr;
        }
    }

    private void UpdateInventoryState() { if (inventoryManager == null) return; bool amIParent = (localPlayer.playerId == currentParentId); inventoryManager.SetActiveState(isGameStarted && isBuildingPhase && amIParent); }

    public void OnAnswerResult(bool isCorrect)
    {
        if (isProcessingResult) return;
        if (isCorrect) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ProcessCorrectAnswer));
        else SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ProcessWrongAnswer));
    }

    public void ProcessTimeUp() { if (isProcessingResult) return; SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ProcessWrongAnswer)); }

    public void ProcessCorrectAnswer()
    {
        isProcessingResult = true; // 同期変数
        isThinkingPhase = false;   // 同期変数

        // テキストをセット
        if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF><size=150%>正解！！</size></color>";

        if (Networking.IsOwner(gameObject))
        {
            citizenWins++;
            RequestSerialization();
            if (citizenWins >= 5) SendCustomEventDelayedSeconds(nameof(GameOverCitizen), 3.0f);
            else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }

        UpdateUI();
    }

    public void ProcessWrongAnswer()
    {
        isProcessingResult = true;
        isThinkingPhase = false;

        if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000><size=150%>不正解...</size></color>";

        if (Networking.IsOwner(gameObject))
        {
            werewolfWins++;
            RequestSerialization();
            if (werewolfWins >= 3) SendCustomEventDelayedSeconds(nameof(GameOverWerewolf), 3.0f);
            else SendCustomEventDelayedSeconds(nameof(StartNextTurn), 3.0f);
        }
        UpdateUI();
    }

    public void GameOverCitizen() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ShowCitizenWin)); }
    public void GameOverWerewolf() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ShowWerewolfWin)); }

    public void ShowCitizenWin() { if (phaseMessageText != null) phaseMessageText.text = "<color=#00FFFF><size=200%>市民チームの勝利！</size></color>"; SendCustomEventDelayedSeconds(nameof(EndGameCleanup), 5.0f); }
    public void ShowWerewolfWin() { if (phaseMessageText != null) phaseMessageText.text = "<color=#FF0000><size=200%>人狼チームの勝利！</size></color>"; SendCustomEventDelayedSeconds(nameof(EndGameCleanup), 5.0f); }

    public void ReturnToLobby()
    {
        if (phaseMessageText != null) phaseMessageText.text = "";
        if (localPlayer != null) localPlayer.TeleportTo(new Vector3(0, -6.0f, 0), Quaternion.identity);
        UpdateUI();
    }

    public void StartNextTurn()
    {
        if (Networking.IsOwner(gameObject))
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkClearBlocks));
            if (topicManager != null) topicManager.DrawNewTopics();
            currentParentId = currentGuesserId;
            PickNewGuesser();
            RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(StartGameSequence), 1.0f);
        }
    }

    public void NetworkClearBlocks() { if (inventoryManager != null) inventoryManager.ClearAllBlocks(); }

    public void EndGameCleanup()
    {
        if (Networking.IsOwner(gameObject))
        {
            isGameStarted = false;
            isBuildingPhase = false;
            isThinkingPhase = false;
            RequestSerialization();
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkClearBlocks));
        }
        if (topicUIRoot != null) topicUIRoot.SetActive(false);
        if (votingUIRoot != null) votingUIRoot.SetActive(false);
        if (timerText != null) timerText.text = "";
        SendCustomEventDelayedSeconds(nameof(ReturnToLobby), 5.0f);
    }
}