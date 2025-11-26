using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// ▼ ここの名前が "SimpleButton" になっていないとエラーになります ▼
public class SimpleButton : UdonSharpBehaviour
{
    public UdonBehaviour targetProgram;
    public string eventName;

    public override void Interact()
    {
        if (targetProgram != null)
        {
            // ログを出して確認しやすくする
            Debug.Log($"[SimpleButton] Sending event '{eventName}' to {targetProgram.name}");
            targetProgram.SendCustomEvent(eventName);
        }
    }
}
