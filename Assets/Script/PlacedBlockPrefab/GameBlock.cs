using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class GameBlock : UdonSharpBehaviour
{
    [UdonSynced] private bool isVisible = false;
    [UdonSynced] private Vector3 syncScale = Vector3.one;

    public bool isOccupied { get { return isVisible; } }

    void Start()
    {
        UpdateVisuals();
    }

    public void Place(Vector3 scale)
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = true;
            syncScale = scale;
            RequestSerialization(); // 値を確定させてから送信
            UpdateVisuals();
        }
    }

    public void ResetBlock()
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = false;
            // 非アクティブにする代わりに、地の果て（地下100m）に飛ばすごり押しで
            transform.position = new Vector3(0, -100f, 0);
            RequestSerialization();
            UpdateVisuals();
        }
    }

    // 他人の画面でデータが届いた時に呼ばれる
    public override void OnDeserialization()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        // レンダラーとコライダーだけをオンオフする
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = isVisible;

        transform.localScale = syncScale;

        // 重要：本体のチェック（SetActive）は常にオンにしておかないと通信が届かない
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }
}