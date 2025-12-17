using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class GameBlock : UdonSharpBehaviour
{
    // 全員に対して「表示しろ！」と命令する
    public void Spawn()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnableBlock");
    }

    // 全員に対して「隠せ！」と命令する
    public void Despawn()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "DisableBlock");
    }

    public void EnableBlock()
    {
        gameObject.SetActive(true);
    }

    public void DisableBlock()
    {
        gameObject.SetActive(false);
    }
}