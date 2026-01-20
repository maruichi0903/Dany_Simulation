// GameBlock.cs (全文)
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
            RequestSerialization(); // 順番に注意：値を決めてからシリアライズ
            UpdateVisuals();
        }
    }

    public void ResetBlock()
    {
        if (Networking.IsOwner(gameObject))
        {
            isVisible = false;
            RequestSerialization();
            UpdateVisuals();
            // ★ SetActive(false)は絶対に使わない！
        }
    }

    public override void OnDeserialization()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        // レンダラーとコライダーだけをオフにする
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = isVisible;

        transform.localScale = syncScale;

        // 本体は常に「アクティブ」にしておかないと通信が届かない
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }
}