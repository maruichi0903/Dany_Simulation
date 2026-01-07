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
        // 同期などで呼ばれた時も、表示フラグが立っているなら本体をオンにする
        if (isVisible && !gameObject.activeSelf) gameObject.SetActive(true);

        transform.localScale = syncScale;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = isVisible;
    }
}