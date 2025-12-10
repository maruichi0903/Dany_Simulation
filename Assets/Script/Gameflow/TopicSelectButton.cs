using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class TopicSelectButton : UdonSharpBehaviour
{
    [Tooltip("このボタンが担当する単語の番号 (0~4)")]
    public int buttonIndex;

    [Tooltip("判定を依頼するマネージャー")]
    public TopicManager topicManager;

    public override void Interact()
    {
        if (topicManager != null)
        {
            // マネージャーに「〇番が選ばれた」と報告用
            topicManager.OnSubmitAnswer(buttonIndex);
        }
    }
}