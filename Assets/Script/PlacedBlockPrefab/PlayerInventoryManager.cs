using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
using TMPro;

public class PlayerInventoryManager : UdonSharpBehaviour
{
    [Header("Managers")]
    public ObjectPoolManager objectPoolManager;

    [Header("Game Settings")]
    [Tooltip("1回のスクロールで回転させる角度 (例: 30度, 45度, 90度)")]
    public float rotationSnapAngle = 30.0f; // ▼▼▼ 変更: 固定角度の設定 ▼▼▼

    public int blockLayer = 0;
    public TextMeshProUGUI stockText;

    [Header("Visual Data")]
    public Sprite[] objectSprites;

    [Header("UI Components")]
    public Image[] slotFrames;
    public Image[] iconImages;

    [Header("Preview Settings")]
    [Tooltip("ゴースト表示に使う半透明マテリアル")]
    public Material ghostMaterial;

    private GameObject currentGhost;

    [Header("Colors")]
    public Color frameNormalColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    public Color frameSelectedColor = new Color(1.0f, 1.0f, 0.0f, 1.0f);

    [Header("Placement Settings")]
    public float maxReachDistance = 8.0f;
    public float gridSize = 1.0f;

    [Header("System Settings")]
    public GameObject hudRoot;
    private bool isInputActive = false;

    // 内部データ
    private int[] handheldInventory = { -1, -1, -1, -1, -1 };
    private int currentSlotIndex = 0;
    private Vector3[] slotRotations = new Vector3[5];

    private float enableTime = 0f;
    private float inputCooldown = 1.0f;

    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer) || !localPlayer.isLocal)
        {
            gameObject.SetActive(false);
            return;
        }

        slotRotations = new Vector3[5];

        SetActiveState(false);
        SetRandomInventory();

        UpdateGhostModel();

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

        if (isActive)
        {
            enableTime = Time.time;
            UpdateGhostModel();
        }

        if (!isActive && currentGhost != null) currentGhost.SetActive(false);
    }

    private void HandleInput()
    {
        if (Time.time < enableTime + inputCooldown) return;

        // 数字キー選択
        int newSlotIndex = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) newSlotIndex = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) newSlotIndex = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) newSlotIndex = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) newSlotIndex = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) newSlotIndex = 4;

        if (newSlotIndex != -1)
        {
            currentSlotIndex = newSlotIndex;
            UpdateGhostModel();
            UpdateSelectionUI();
        }

        // ▼▼▼ 修正: スナップ回転処理 (固定角度で回す) ▼▼▼
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0.0f)
        {
            // スクロールの方向を判定 (正なら1, 負なら-1)
            float direction = Mathf.Sign(scroll);

            // 固定角度を適用
            float rotAmount = direction * rotationSnapAngle;

            if (Input.GetKey(KeyCode.Q))
            {
                slotRotations[currentSlotIndex].x += rotAmount;
            }
            else if (Input.GetKey(KeyCode.F))
            {
                slotRotations[currentSlotIndex].z += rotAmount;
            }
            else
            {
                slotRotations[currentSlotIndex].y += rotAmount;
            }

            // 角度をきれいに丸める処理（誤差蓄積防止）
            // 例: 29.999度になってしまっても、30度に補正する
            slotRotations[currentSlotIndex].x = Mathf.Round(slotRotations[currentSlotIndex].x / rotationSnapAngle) * rotationSnapAngle;
            slotRotations[currentSlotIndex].y = Mathf.Round(slotRotations[currentSlotIndex].y / rotationSnapAngle) * rotationSnapAngle;
            slotRotations[currentSlotIndex].z = Mathf.Round(slotRotations[currentSlotIndex].z / rotationSnapAngle) * rotationSnapAngle;
        }
        // ▲▲▲ ▲▲▲

        // 配置
        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
        {
            TryPlaceCurrentObject();
        }

        // 手元に戻す
        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1))
        {
            TryReturnObjectToHand();
        }
    }

    private void UpdateGhostModel()
    {
        if (currentGhost != null)
        {
            Destroy(currentGhost);
        }

        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1 || objectPoolManager == null) return;

        GameObject prefab = objectPoolManager.objectPrefabs[objID];
        if (prefab == null) return;

        currentGhost = Instantiate(prefab);

        Destroy(currentGhost.GetComponent<Collider>());
        Destroy(currentGhost.GetComponent<Rigidbody>());
        Destroy(currentGhost.GetComponent<VRCObjectSync>());
        Destroy(currentGhost.GetComponent<UdonBehaviour>());

        Collider[] childCols = currentGhost.GetComponentsInChildren<Collider>();
        foreach (Collider c in childCols) Destroy(c);

        if (ghostMaterial != null)
        {
            MeshRenderer mr = currentGhost.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = ghostMaterial;

            MeshRenderer[] childRenderers = currentGhost.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer r in childRenderers) r.sharedMaterial = ghostMaterial;
        }

        currentGhost.layer = 2; // Ignore Raycast
        currentGhost.SetActive(isInputActive);
    }

    private void UpdateGhostPosition()
    {
        if (currentGhost == null) return;
        if (handheldInventory[currentSlotIndex] == -1)
        {
            currentGhost.SetActive(false);
            return;
        }

        Vector3 targetPos = CalculateFreePosition();

        currentGhost.SetActive(true);
        currentGhost.transform.position = targetPos;
        currentGhost.transform.rotation = Quaternion.Euler(slotRotations[currentSlotIndex]);
    }

    private void TryPlaceCurrentObject()
    {
        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1) return;

        Vector3 spawnPosition = CalculateFreePosition();

        GameObject objToSpawn = objectPoolManager.GetNextBlock(objID);

        if (objToSpawn != null)
        {
            Networking.SetOwner(localPlayer, objToSpawn);

            Rigidbody rb = objToSpawn.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            objToSpawn.transform.rotation = Quaternion.Euler(slotRotations[currentSlotIndex]);
            objToSpawn.transform.position = spawnPosition;
            objToSpawn.SetActive(true);

            handheldInventory[currentSlotIndex] = -1;
            slotRotations[currentSlotIndex] = Vector3.zero;

            UpdateSelectionUI();
            UpdateStockUI();
            UpdateGhostModel();
        }
    }

    private void TryReturnObjectToHand()
    {
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 startPos = headData.position;
        Vector3 dir = headData.rotation * Vector3.forward;

        RaycastHit hit;
        if (Physics.Raycast(startPos, dir, out hit, maxReachDistance))
        {
            GameObject hitObj = hit.collider.gameObject;

            if (hitObj.layer == blockLayer)
            {
                int emptySlot = -1;
                for (int i = 0; i < handheldInventory.Length; i++)
                {
                    if (handheldInventory[i] == -1)
                    {
                        emptySlot = i;
                        break;
                    }
                }

                if (emptySlot != -1)
                {
                    int objID = GetObjectIdFromInstance(hitObj);
                    if (objID != -1)
                    {
                        handheldInventory[emptySlot] = objID;
                        slotRotations[emptySlot] = Vector3.zero;

                        Networking.SetOwner(localPlayer, hitObj);
                        hitObj.SetActive(false);
                        UpdateSelectionUI();
                        UpdateStockUI();
                        UpdateGhostModel();
                    }
                }
            }
        }
    }

    private int GetObjectIdFromInstance(GameObject instance)
    {
        if (instance == null || objectPoolManager == null) return -1;
        string instanceName = instance.name;

        for (int i = 0; i < objectPoolManager.objectPrefabs.Length; i++)
        {
            GameObject prefab = objectPoolManager.objectPrefabs[i];
            if (prefab != null && instanceName.StartsWith(prefab.name))
            {
                return i;
            }
        }
        return -1;
    }

    private Vector3 CalculateFreePosition()
    {
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 startPos = headData.position;
        Vector3 dir = headData.rotation * Vector3.forward;

        RaycastHit hit;
        int layerMask = ~(1 << blockLayer);

        if (Physics.Raycast(startPos, dir, out hit, maxReachDistance, layerMask))
        {
            return hit.point;
        }
        else
        {
            return startPos + (dir * maxReachDistance);
        }
    }

    public void SetRandomInventory()
    {
        if (objectPoolManager == null) return;
        int typeCount = objectPoolManager.objectPrefabs.Length;
        for (int i = 0; i < handheldInventory.Length; i++)
        {
            handheldInventory[i] = Random.Range(0, typeCount);
            slotRotations[i] = Vector3.zero;
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
            int currentCount = 0;
            foreach (int id in handheldInventory) if (id != -1) currentCount++;
            stockText.text = "Hand: " + currentCount.ToString() + " / 5";
        }
    }

    public void ClearAllBlocks()
    {
        if (objectPoolManager == null || objectPoolManager.objectPools == null) return;

        for (int i = 0; i < objectPoolManager.objectPools.Length; i++)
        {
            if (objectPoolManager.objectPools[i] == null) continue;
            for (int j = 0; j < objectPoolManager.objectPools[i].Length; j++)
            {
                if (objectPoolManager.objectPools[i][j] != null)
                {
                    objectPoolManager.objectPools[i][j].SetActive(false);
                }
            }
        }
    }
}