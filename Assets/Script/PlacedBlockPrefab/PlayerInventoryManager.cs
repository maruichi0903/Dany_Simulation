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
    [Tooltip("回転速度")]
    public float rotationSpeed = 20.0f;
    public int blockLayer = 0;
    public TextMeshProUGUI stockText;

    [Header("Visual Data")]
    public Sprite[] objectSprites;

    [Header("UI Components")]
    public Image[] slotFrames;
    public Image[] iconImages;

    [Header("Preview Settings")]
    public GameObject previewGhostPrefab;
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

        if (previewGhostPrefab != null)
        {
            currentGhost = Instantiate(previewGhostPrefab);
            currentGhost.SetActive(false);
        }

        // 配列の初期化（念のため）
        slotRotations = new Vector3[5];

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
        if (isActive)
        {
            enableTime = Time.time;
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
            UpdateSelectionUI();
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0.0f)
        {
            float rotAmount = scroll * rotationSpeed * 10.0f;

            if (Input.GetKey(KeyCode.Q))
            {
                // Qキー + ホイール: X軸（縦）回転
                slotRotations[currentSlotIndex].x += rotAmount;
            }
            else if (Input.GetKey(KeyCode.F))
            {
                // Fキー + ホイール: Z軸（傾き）回転
                slotRotations[currentSlotIndex].z += rotAmount;
            }
            else
            {
                // 何も押していない時は Y軸（横）回転
                slotRotations[currentSlotIndex].y += rotAmount;
            }
        }

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

    private void UpdateGhostPosition()
    {
        if (currentGhost == null) return;
        if (handheldInventory[currentSlotIndex] == -1)
        {
            currentGhost.SetActive(false);
            return;
        }

        Vector3 targetPos = CalculateGridPosition();

        currentGhost.SetActive(true);
        currentGhost.transform.position = targetPos;
        currentGhost.transform.rotation = Quaternion.Euler(slotRotations[currentSlotIndex]);
    }

    private void TryPlaceCurrentObject()
    {
        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1) return;

        Vector3 spawnPosition = CalculateGridPosition();
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

            objToSpawn.transform.localScale = Vector3.one;
            objToSpawn.transform.rotation = Quaternion.Euler(slotRotations[currentSlotIndex]);
            objToSpawn.transform.position = spawnPosition;
            objToSpawn.SetActive(true);

            handheldInventory[currentSlotIndex] = -1;
            slotRotations[currentSlotIndex] = Vector3.zero;

            UpdateSelectionUI();
            UpdateStockUI();
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

    private Vector3 CalculateGridPosition()
    {
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 startPos = headData.position;
        Vector3 dir = headData.rotation * Vector3.forward;

        RaycastHit hit;
        int layerMask = ~0;
        //Vector3 targetRawPos;

        if (Physics.Raycast(startPos, dir, out hit, maxReachDistance, layerMask))
        {
            //float safeGridSize = (gridSize > 0.001f) ? gridSize : 1.0f;
            //targetRawPos = hit.point + (hit.normal * (gridSize / 2.0f));
            return hit.point;
        }
        else
        {
            //targetRawPos = startPos + (dir * maxReachDistance);
            return startPos + (dir * maxReachDistance);
        }

        //if (gridSize <= 0.001f) return targetRawPos;

        //float x = Mathf.Round(targetRawPos.x / gridSize) * gridSize;
        //float y = Mathf.Round(targetRawPos.y / gridSize) * gridSize;
        //float z = Mathf.Round(targetRawPos.z / gridSize) * gridSize;

        //return new Vector3(x, y, z);
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
}
