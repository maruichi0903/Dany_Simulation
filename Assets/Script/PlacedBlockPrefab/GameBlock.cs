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
            // ★【重要】ここでオブジェクト本体を「オン」にする！
            // これがないと、Rendererをオンにしても表示されません。
            gameObject.SetActive(true);

            isVisible = true;
            syncScale = scale;
            UpdateVisuals();
            RequestSerialization();
        }
    }

    public void ResetBlock()
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = false;
            UpdateVisuals();
            RequestSerialization();

            // 片付ける時は、誤動作防止のため本体ごとオフにする
            gameObject.SetActive(false);
        }
    }

    public override void OnDeserialization()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        // RendererとColliderだけで表示・非表示を切り替える
        // ※本体のSetActive(false)は極力避ける（同期が止まるため）
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = isVisible;

        transform.localScale = syncScale;

        // もしどうしてもSetActiveを使いたい場合は、オーナー以外が勝手にいじらないようにする
        if (isVisible)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }
    }
}