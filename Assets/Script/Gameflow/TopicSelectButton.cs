using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// TopicSelectButton.cs
public class TopicSelectButton : UdonSharpBehaviour
{
    public int buttonIndex;
    public TopicManager topicManager;

    // Interactの代わりに、クリック専用の関数を作る
    public void OnButtonClick()
    {
        if (topicManager != null)
        {
            topicManager.OnSubmitAnswer(buttonIndex);
        }
    }

    // Interactも残しておけば、近づいてEキーで押すことも可能です
    public override void Interact()
    {
        OnButtonClick();
    }
}