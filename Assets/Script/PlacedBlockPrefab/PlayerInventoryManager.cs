using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public class PlayerInventoryManager : UdonSharpBehaviour
{
    [Header("Managers")]
    public ObjectPoolManager objectPoolManager;

    [Header("Game Settings")]
    public int totalStock = 100;
    public int blockLayer = 0;
    public TextMeshProUGUI stockText;

    [Header("Visual Data")]
    public Sprite[] objectSprites;

    [Header("UI Components")]
    public Image[] slotFrames;
    public Image[] iconImages;

    [Header("Preview Settings")]
    public GameObject previewGhostPrefab;
    private GameObject currentGhost; // 実際に表示するゴースト

    [Header("Colors")]
    public Color frameNormalColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    public Color frameSelectedColor = new Color(1.0f, 1.0f, 0.0f, 1.0f);

    [Header("Placement Settings")]
    public float maxReachDistance = 8.0f;
    public float gridSize = 1.0f;

    [Header("System Settings")]
    [Tooltip("インベントリUI全体をまとめた親オブジェクト")]
    public GameObject hudRoot;
    private bool isInputActive = false;

    // 内部データ
    private int[] handheldInventory = { -1, -1, -1, -1, -1 };
    private int currentSlotIndex = 0;
    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer) || !localPlayer.isLocal)
        {
            gameObject.SetActive(false);
            return;
        }
        // ゴーストの生成
        if (previewGhostPrefab != null)
        {
            currentGhost = Instantiate(previewGhostPrefab);
            currentGhost.SetActive(false);
        }

        SetActiveState(false);

        SetRandomInventory();
        UpdateSelectionUI();
        UpdateStockUI();
    }

    void Update()
    {
        if (!localPlayer.isLocal) return;
        if (!isInputActive) return;
        HandleInput();
        UpdateGhostPosition();
    }
    public void SetActiveState(bool isActive)
    {
        isInputActive = isActive;
        if (hudRoot != null) hudRoot.SetActive(isActive);
        
        // ゴーストも消しておく
        if (!isActive && currentGhost != null) currentGhost.SetActive(false);
    }

    private void HandleInput()
    {
        // 数字キー1~5: スロット選択
        int newSlotIndex = -1;

        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) newSlotIndex = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) newSlotIndex = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) newSlotIndex = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) newSlotIndex = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) newSlotIndex = 4;

        if (newSlotIndex != -1)
        {
            currentSlotIndex = newSlotIndex;
            UpdateSelectionUI();
        }

        // Eキー左クリック: 配置
        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
        {
            TryPlaceCurrentObject();
        }

        // Rキー右クリック: 削除
        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1))
        {
            TryRemoveObject();
        }
    }

    private void UpdateGhostPosition()
    {
        if (currentGhost == null) return;

        // 現在の手持ちが空ならゴーストを消す
        if (handheldInventory[currentSlotIndex] == -1)
        {
            currentGhost.SetActive(false);
            return;
        }

        // 位置計算
        Vector3 targetPos = CalculateGridPosition();

        // 重なりチェック（置ける場所かどうか）
        Collider[] hitColliders = Physics.OverlapSphere(targetPos, gridSize * 0.45f);
        bool canPlace = (hitColliders.Length == 0);

        // ゴーストの表示・移動
        currentGhost.SetActive(true);
        currentGhost.transform.position = targetPos;
        currentGhost.transform.rotation = Quaternion.identity;

        // 置けない場所なら少し色を変えたり消したりする工夫も可能だが
        // ここではシンプルに「置けない場所でも位置だけは表示」しておく
    }
    private void TryPlaceCurrentObject()
    {
        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1) return;

        Vector3 spawnPosition = CalculateGridPosition();
        Collider[] hitColliders = Physics.OverlapSphere(spawnPosition, gridSize * 0.45f);
        if (hitColliders.Length > 0) return; // 何かあったら置けない

        // オブジェクト生成
        GameObject objToSpawn = objectPoolManager.GetNextBlock(objID);

        if (objToSpawn != null)
        {
            Networking.SetOwner(localPlayer, objToSpawn);
            objToSpawn.transform.localScale = Vector3.one;
            objToSpawn.transform.rotation = Quaternion.identity;
            objToSpawn.transform.position = spawnPosition;
            objToSpawn.SetActive(true);

            // ストック処理
            // 1. 手持ちを空にする
            handheldInventory[currentSlotIndex] = -1;

            // 2. ストックが残っていれば補充する
            if (totalStock > 0)
            {
                // ストックを減らす
                totalStock--;

                // 新しいアイテムを補充
                int typeCount = objectPoolManager.objectPrefabs.Length;
                handheldInventory[currentSlotIndex] = Random.Range(0, typeCount);
            }

            UpdateSelectionUI();
            UpdateStockUI();
        }
    }

    private void TryRemoveObject()
    {
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 startPos = headData.position;
        Vector3 dir = headData.rotation * Vector3.forward;

        RaycastHit hit;

        // Raycastで当たったオブジェクトを取得
        if (Physics.Raycast(startPos, dir, out hit, maxReachDistance))
        {
            GameObject hitObj = hit.collider.gameObject;

            // ▼▼▼ 修正箇所: Layer番号での比較に置き換え ▼▼▼
            // UdonではLayer番号での比較が最も確実
            if (hitObj.layer == blockLayer)
            {
                // 削除（非アクティブ化）するには所有権が必要
                Networking.SetOwner(localPlayer, hitObj);
                hitObj.SetActive(false);
            }
            // ▲▲▲ 修正箇所 ▲▲▲
        }
    }

    private Vector3 CalculateGridPosition()
    {
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 startPos = headData.position;
        Vector3 dir = headData.rotation * Vector3.forward;

        RaycastHit hit;
        int layerMask = ~0;
        Vector3 targetRawPos;

        if (Physics.Raycast(startPos, dir, out hit, maxReachDistance, layerMask))
        {
            // 当たった面の隣(手前/上など)を中心とする
            float safeGridSize = (gridSize > 0.001f) ? gridSize : 1.0f;
            targetRawPos = hit.point + (hit.normal * (gridSize / 2.0f));
        }
        else
        {
            // 空中
            targetRawPos = startPos + (dir * maxReachDistance);
        }

        if (gridSize <= 0.001f)
        {
            return targetRawPos;
        }

        // スナップ処理
        float x = Mathf.Round(targetRawPos.x / gridSize) * gridSize;
        float y = Mathf.Round(targetRawPos.y / gridSize) * gridSize;
        float z = Mathf.Round(targetRawPos.z / gridSize) * gridSize;

        return new Vector3(x, y, z);
    }

    public void SetRandomInventory()
    {
        if (objectPoolManager == null) return;
        int typeCount = objectPoolManager.objectPrefabs.Length;

        for (int i = 0; i < handheldInventory.Length; i++)
        {
            // ストックがある限り配る
            if (totalStock > 0)
            {
                handheldInventory[i] = Random.Range(0, typeCount);
                totalStock--;
            }
            else
            {
                handheldInventory[i] = -1;
            }
        }
    }

    private void UpdateSelectionUI()
    {
        for (int i = 0; i < 5; i++)
        {
            if (slotFrames.Length > i && slotFrames[i] != null)
                slotFrames[i].color = (i == currentSlotIndex) ? frameSelectedColor : frameNormalColor;

            if (iconImages.Length > i && iconImages[i] != null)
            {
                int objID = handheldInventory[i];
                if (objID != -1)
                {
                    iconImages[i].enabled = true;
                    if (objectSprites.Length > objID) iconImages[i].sprite = objectSprites[objID];
                }
                else iconImages[i].enabled = false;
            }
        }
    }
    private void UpdateStockUI()
    {
        if (stockText != null)
        {
            stockText.text = "Stock: " + totalStock.ToString();
        }
    }
}
