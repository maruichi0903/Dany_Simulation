using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class GameBlock : UdonSharpBehaviour
{
    // ▼▼▼ 命令ではなく「データ」で管理する ▼▼▼
    [UdonSynced] private bool isVisible = false;
    [UdonSynced] private Vector3 syncScale = Vector3.one;

    public bool isOccupied { get { return isVisible; } } // 外部参照用

    void Start()
    {
        // 初期状態を反映
        UpdateVisuals();
    }

    // ★ 配置する時に呼ぶ関数（オーナーのみ実行）
    public void Place(Vector3 scale)
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = true;        // 表示フラグON
            syncScale = scale;       // 大きさセット

            // ローカル反映
            UpdateVisuals();

            // データ送信（全員に「今は表示中で、この大きさだよ」と伝える）
            RequestSerialization();
        }
    }

    // ★ 片付ける時に呼ぶ関数（オーナーのみ実行）
    public void ResetBlock()
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = false;       // 表示フラグOFF

            // ローカル反映
            UpdateVisuals();

            // データ送信
            RequestSerialization();
        }
    }

    // ★ データを受け取った全員が実行する関数
    public override void OnDeserialization()
    {
        UpdateVisuals();
    }

    // ★ 実際の見た目を切り替える処理
    private void UpdateVisuals()
    {
        // 大きさを適用
        transform.localScale = syncScale;

        // 見た目（Renderer）と当たり判定（Collider）を切り替え
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = isVisible;

        // ※ VRCObjectSyncは切らない（位置ズレ防止のため）
    }
}