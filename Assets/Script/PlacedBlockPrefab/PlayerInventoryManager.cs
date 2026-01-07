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
    public float rotationSnapAngle = 30.0f;
    public float scaleStep = 0.1f;
    public int blockLayer = 0;
    public TextMeshProUGUI stockText;

    [Header("Visual Data")]
    public Sprite[] objectSprites;

    [Header("UI Components")]
    public Image[] slotFrames;
    public Image[] iconImages;

    [Header("Preview Settings")]
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

    private int[] handheldInventory = new int[5];
    private int[] reserveInventory = new int[5];
    private int reserveCount = 0;

    private Vector3[] slotRotations = new Vector3[5];
    private float[] slotScales = new float[5];

    private int currentSlotIndex = 0;
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

        handheldInventory = new int[5];
        reserveInventory = new int[5];
        slotRotations = new Vector3[5];
        slotScales = new float[5];

        for (int i = 0; i < 5; i++)
        {
            handheldInventory[i] = -1;
            slotRotations[i] = Vector3.zero;
            slotScales[i] = 1.0f;
            reserveInventory[i] = -1;
        }

        // スタート時はUIを隠す
        if (hudRoot != null) hudRoot.SetActive(false);

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

    public void RefillInventory()
    {
        if (objectPoolManager == null) return;
        int typeCount = objectPoolManager.objectPrefabs.Length;

        for (int i = 0; i < 5; i++)
        {
            handheldInventory[i] = Random.Range(0, typeCount);
            slotRotations[i] = Vector3.zero;
            slotScales[i] = 1.0f;
        }
        for (int i = 0; i < 5; i++)
        {
            reserveInventory[i] = Random.Range(0, typeCount);
        }
        reserveCount = 5;

        currentSlotIndex = 0;
        UpdateGhostModel();
        UpdateSelectionUI();
        UpdateStockUI();
    }

    private void TryRefillSlot(int slotIndex)
    {
        if (reserveCount > 0)
        {
            int nextItem = reserveInventory[reserveCount - 1];
            reserveInventory[reserveCount - 1] = -1;
            reserveCount--;

            handheldInventory[slotIndex] = nextItem;
            slotRotations[slotIndex] = Vector3.zero;
            slotScales[slotIndex] = 1.0f;
        }
    }

    public void SetActiveState(bool isActive)
    {
        isInputActive = isActive;
        if (hudRoot != null) hudRoot.SetActive(isActive);
        if (isActive) { enableTime = Time.time; UpdateGhostModel(); }
        if (!isActive && currentGhost != null) currentGhost.SetActive(false);
    }

    private void HandleInput()
    {
        if (Time.time < enableTime + inputCooldown) return;

        int newSlotIndex = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) newSlotIndex = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) newSlotIndex = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) newSlotIndex = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) newSlotIndex = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) newSlotIndex = 4;

        if (newSlotIndex != -1)
        {
            currentSlotIndex = newSlotIndex;
            UpdateGhostModel();
            UpdateSelectionUI();
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0.0f)
        {
            float direction = Mathf.Sign(scroll);
            if (Input.GetKey(KeyCode.Q))
            {
                float newScale = slotScales[currentSlotIndex] + (direction * scaleStep);
                slotScales[currentSlotIndex] = Mathf.Clamp(newScale, 0.2f, 3.0f);
            }
            else if (Input.GetKey(KeyCode.F))
            {
                float rotAmount = direction * rotationSnapAngle;
                slotRotations[currentSlotIndex].y += rotAmount;
                slotRotations[currentSlotIndex].y = Mathf.Round(slotRotations[currentSlotIndex].y / rotationSnapAngle) * rotationSnapAngle;
            }
            else
            {
                float rotAmount = direction * rotationSnapAngle;
                slotRotations[currentSlotIndex].z += rotAmount;
                slotRotations[currentSlotIndex].z = Mathf.Round(slotRotations[currentSlotIndex].z / rotationSnapAngle) * rotationSnapAngle;
            }
            UpdateGhostScale();
        }

        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)) TryPlaceCurrentObject();
        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1)) TryReturnObjectToHand();
    }

    private void UpdateGhostScale()
    {
        if (currentGhost == null) return;
        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1 || objectPoolManager == null) return;

        GameObject prefab = objectPoolManager.objectPrefabs[objID];
        Vector3 baseScale = prefab.transform.localScale;

        float s = slotScales[currentSlotIndex];
        if (s <= 0.01f) s = 1.0f;

        currentGhost.transform.localScale = baseScale * s;
    }

    private void UpdateGhostModel()
    {
        if (currentGhost != null) Destroy(currentGhost);
        if (currentSlotIndex < 0 || currentSlotIndex >= handheldInventory.Length) return;

        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1 || objectPoolManager == null) return;

        GameObject prefab = objectPoolManager.objectPrefabs[objID];
        if (prefab == null) return;

        currentGhost = Instantiate(prefab);

        Destroy(currentGhost.GetComponent<Collider>());
        Destroy(currentGhost.GetComponent<Rigidbody>());
        Destroy(currentGhost.GetComponent<VRCObjectSync>());
        Destroy(currentGhost.GetComponent<UdonBehaviour>());
        foreach (Collider c in currentGhost.GetComponentsInChildren<Collider>()) Destroy(c);

        if (ghostMaterial != null)
        {
            foreach (MeshRenderer r in currentGhost.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = ghostMaterial;
            foreach (SkinnedMeshRenderer r in currentGhost.GetComponentsInChildren<SkinnedMeshRenderer>()) r.sharedMaterial = ghostMaterial;
        }

        currentGhost.layer = 2;
        currentGhost.SetActive(isInputActive);

        UpdateGhostScale();
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

        UpdateGhostScale();
    }

    // ▼▼▼ 修正箇所：物理演算のエラーを防ぐ ▼▼▼
    private void TryPlaceCurrentObject()
    {
        int objID = handheldInventory[currentSlotIndex];
        if (objID == -1) return;

        Vector3 spawnPosition = CalculateFreePosition();
        // ここでnullが返ってくると「No free object found」エラーになる
        GameObject objToSpawn = objectPoolManager.GetNextBlock(objID);

        if (objToSpawn != null)
        {
            Networking.SetOwner(localPlayer, objToSpawn);

            Rigidbody rb = objToSpawn.GetComponent<Rigidbody>();

            // ★修正：Kinematic（物理無効）なら速度をいじらない
            if (rb != null && !rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 念のため固定
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            objToSpawn.transform.rotation = Quaternion.Euler(slotRotations[currentSlotIndex]);
            objToSpawn.transform.position = spawnPosition;

            GameObject prefab = objectPoolManager.objectPrefabs[objID];
            Vector3 baseScale = prefab.transform.localScale;
            float s = slotScales[currentSlotIndex];
            if (s <= 0.01f) s = 1.0f;
            Vector3 finalScale = baseScale * s;

            GameBlock block = objToSpawn.GetComponent<GameBlock>();
            if (block != null)
            {
                block.Place(finalScale);
            }
            else
            {
                objToSpawn.SetActive(true);
                objToSpawn.transform.localScale = finalScale;
            }

            handheldInventory[currentSlotIndex] = -1;
            TryRefillSlot(currentSlotIndex);

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
                int objID = GetObjectIdFromInstance(hitObj);
                if (objID == -1) return;

                int targetSlot = -1;
                for (int i = 0; i < 5; i++)
                {
                    if (handheldInventory[i] == -1)
                    {
                        targetSlot = i;
                        break;
                    }
                }

                if (targetSlot == -1)
                {
                    if (reserveCount < 5)
                    {
                        int itemToPush = handheldInventory[currentSlotIndex];
                        reserveInventory[reserveCount] = itemToPush;
                        reserveCount++;
                        targetSlot = currentSlotIndex;
                    }
                    else return;
                }

                if (targetSlot != -1)
                {
                    handheldInventory[targetSlot] = objID;
                    slotRotations[targetSlot] = Vector3.zero;
                    slotScales[targetSlot] = 1.0f;

                    Networking.SetOwner(localPlayer, hitObj);

                    GameBlock block = hitObj.GetComponent<GameBlock>();
                    if (block != null) block.ResetBlock();
                    else hitObj.SetActive(false);

                    UpdateSelectionUI();
                    UpdateStockUI();
                    UpdateGhostModel();
                }
            }
        }
    }

    private int GetObjectIdFromInstance(GameObject instance) { if (instance == null || objectPoolManager == null) return -1; string instanceName = instance.name; for (int i = 0; i < objectPoolManager.objectPrefabs.Length; i++) { GameObject prefab = objectPoolManager.objectPrefabs[i]; if (prefab != null && instanceName.StartsWith(prefab.name)) { return i; } } return -1; }
    private Vector3 CalculateFreePosition() { VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head); Vector3 startPos = headData.position; Vector3 dir = headData.rotation * Vector3.forward; RaycastHit hit; int layerMask = ~(1 << blockLayer); if (Physics.Raycast(startPos, dir, out hit, maxReachDistance, layerMask)) { return hit.point; } else { return startPos + (dir * maxReachDistance); } }

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
            int handCount = 0;
            foreach (int id in handheldInventory) if (id != -1) handCount++;
            int total = handCount + reserveCount;
            stockText.text = "Stock: " + total.ToString() + " / 10";
        }
    }

    public void ClearAllBlocks()
    {
        if (objectPoolManager == null || objectPoolManager.scenePoolObjects == null) return;

        foreach (GameBlock block in objectPoolManager.scenePoolObjects)
        {
            if (block != null && block.isOccupied)
            {
                block.ResetBlock();
            }
        }
    }
}