
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public class GameFlowManager : UdonSharpBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statusText; //ゲーム状態と親の名前を表示するテキスト
    public TextMeshProUGUI myRoleText; //自分にしか見えない役職を表示するテキスト
    public GameObject joinButton; //参加ボタン
    public GameObject startButton; //開始ボタン

    [Header("Game Settings")]
    public int werewolfCount = 1; //人狼の数

    [Header("Managers")]
    public PlayerInventoryManager inventoryManager;

    [Header("Lobby UI")]
    public GameObject lobbyCanvasRoot;

    // 内部データ同期変数
    [UdonSynced] private int[] playerIds = new int[20]; //参加しているプレイヤーのIDリスト
    [UdonSynced] private int playerCount = 0; //参加しているプレイヤーの数
    [UdonSynced] private int[] playerRoles = new int [20]; //各プレイヤーの役職リスト（0:村人, 1:人狼）
    [UdonSynced] private int currentParentId = -1; //現在の親のプレイヤーID
    [UdonSynced] private bool isGameStarted = false;

    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        UpdateUI();
    }

    public override void OnDeserialization()
    {
        UpdateUI();
    }

    // Udonで配列同期を安定させるため、参加処理を少し変更します。
    // 「Joinボタンを押した人が、自分を配列に加えてSyncする」権限を持ちます。
    public void JoinGame()
    {
        // ▼ デバッグログを追加 ▼
        Debug.Log("[GameFlowManager] JoinGame called!");

        // localPlayerが取れているかチェック
        if (localPlayer == null)
        {
            Debug.LogError("[GameFlowManager] Error: localPlayer is NULL! (ClientSimは動いていますか？)");
            return;
        }

        // ゲーム開始済みかチェック
        if (isGameStarted)
        {
            Debug.Log("[GameFlowManager] Rejected: Game already started.");
            return;
        }

        // 既に参加済みかチェック
        bool joined = false;
        for (int i = 0; i < playerCount; i++)
        {
            if (playerIds[i] == localPlayer.playerId) joined = true;
        }

        if (joined)
        {
            Debug.Log("[GameFlowManager] Rejected: You have already joined.");
            return;
        }

        Debug.Log("[GameFlowManager] Success: Joining game...");

        // 自分がオーナーになってデータを更新する
        Networking.SetOwner(localPlayer, gameObject);
        playerIds[playerCount] = localPlayer.playerId;
        playerCount++;
        RequestSerialization(); // 全員に同期
        UpdateUI();
    }

    // --- 2. ゲーム開始 & 役割抽選 ---

    public void OnClickStart()
    {
        Debug.Log("[GameFlowManager] Start command received!");
        // マスターのみ実行可能
        if (!Networking.IsOwner(localPlayer, gameObject)) return;
        if (playerCount < 1) // 元は < 2 でした
        {
            Debug.Log("[GameFlowManager] Not enough players! Count: " + playerCount);
            return;
        } // 最低2人必要,一旦1人に変更

        // 1. 役割の初期化 (全員市民:0)
        for (int i = 0; i < 20; i++) playerRoles[i] = 0;

        // 2. 人狼の抽選
        // ランダムに選んだインデックスを人狼(1)にする
        // 重複しないように選ぶ
        int assignedWerewolves = 0;
        while (assignedWerewolves < werewolfCount)
        {
            int rnd = Random.Range(0, playerCount);
            if (playerRoles[rnd] == 0) // まだ市民なら
            {
                playerRoles[rnd] = 1; // 人狼にする
                assignedWerewolves++;
            }
        }

        // 3. 最初の親の抽選
        int parentIndex = Random.Range(0, playerCount);
        currentParentId = playerIds[parentIndex];

        // 4. ゲーム開始フラグ
        isGameStarted = true;

        RequestSerialization(); // 全員に結果を送信
        UpdateUI();
    }

    // --- 3. UI表示更新 ---

    public void UpdateUI()
    {
        // 1. ロビーUI（参加ボタンなど）の表示切り替え
        // ゲームが始まっていないなら表示、始まっていたら消す
        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(!isGameStarted);

        // 2. テキスト表示の更新
        if (!isGameStarted)
        {
            if (statusText != null) statusText.text = "Waiting... (" + playerCount + " Joined)";
            if (myRoleText != null) myRoleText.text = "";

            // ★待機中はインベントリを無効化
            if (inventoryManager != null) inventoryManager.SetActiveState(false);
        }
        else
        {
            // --- ゲーム中の処理 ---

            string parentName = "Unknown";
            VRCPlayerApi parentPlayer = VRCPlayerApi.GetPlayerById(currentParentId);
            if (Utilities.IsValid(parentPlayer)) parentName = parentPlayer.displayName;

            if (statusText != null) statusText.text = "Current Parent: " + parentName;

            if (Utilities.IsValid(localPlayer))
            {
                // ... (役割確認ロジックはそのまま) ...

                // ★自分が親(Parent)である場合のみ、インベントリを表示・操作可能にする
                bool amIParent = (localPlayer.playerId == currentParentId);

                if (inventoryManager != null)
                {
                    // 親なら True (表示)、それ以外なら False (非表示)
                    inventoryManager.SetActiveState(amIParent);
                }

                // ... (役割テキスト表示ロジックはそのまま) ...
            }
        }
    }
}
