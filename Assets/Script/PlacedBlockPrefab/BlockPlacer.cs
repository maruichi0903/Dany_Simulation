using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class BlockPlacer : UdonSharpBehaviour
{
    [Tooltip("オブジェクトプールマネージャーへの参照")]
    public ObjectPoolManager objectPoolManager;
    [Tooltip("ブロックを配置する視線からの距離")]
    public float placementDistance = 4.0f;
    [Tooltip("ブロックを配置するグリッドサイズ (1.0fで1m単位)")]
    public float gridSize = 1.0f;

    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }

    void Update()
    {
        if (localPlayer == null) return;

        // 一旦Eキーでブロックを配置
        if (Input.GetKeyDown(KeyCode.E))
        {
            PlaceBlock();
        }

        // VRChatコントローラーのトリガーボタン検出（例: Primary Useボタン）
        // if (Input.GetButtonDown("Oculus_CrossPlatform_PrimaryIndexTrigger"))
        // {
        //     PlaceBlock();
        // }
    }

    public void PlaceBlock()
    {
        Vector3 placementPosition = CalculatePlacementPosition();
        GameObject newBlock = objectPoolManager.GetNextBlock(0);
        newBlock.transform.position = placementPosition;
        newBlock.SetActive(true);

        // 5. 【重要】ネットワーク同期と所有権の設定
        // VRChatでは、ブロックを動かしたり、他のプレイヤーに見えるようにしたりするためには、ネットワークの所有権を設定する必要がある
        Networking.SetOwner(localPlayer, newBlock);

        // 他のプレイヤーにブロックの生成（アクティブ化）を同期させるために、
        // ブロック側のUdonSharpスクリプトで同期処理を行う必要がある（UdonSync利用）
        // まずはローカルで動作確認し、次に同期
        Debug.Log("Block Placed at: " + placementPosition.ToString());
    }

    private Vector3 CalculatePlacementPosition()
    {
        // プレイヤーの頭のトラッキングデータ（位置と回転）
        VRCPlayerApi.TrackingData headData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        // 目の位置 (headData.position) から、視線の方向 (headData.rotation * Vector3.forward) に、
        // 設定した距離 (placementDistance) だけ進んだ位置を計算
        Vector3 rawPosition = headData.position + (headData.rotation * Vector3.forward * placementDistance);
        // グリッド配置へのスナップ処理 (マインクラフト的な正確な配置)
        if (gridSize > 0.001f)
        {
            // X, Y, Z座標をそれぞれグリッドサイズで丸める
            rawPosition.x = Mathf.Round(rawPosition.x / gridSize) * gridSize;
            rawPosition.y = Mathf.Round(rawPosition.y / gridSize) * gridSize;
            rawPosition.z = Mathf.Round(rawPosition.z / gridSize) * gridSize;
        }

        return rawPosition;
    }
}
