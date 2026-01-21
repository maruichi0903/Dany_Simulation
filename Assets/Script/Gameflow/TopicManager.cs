using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using UnityEngine.UI;

public class TopicManager : UdonSharpBehaviour
{
    [Header("Managers")]
    public GameFlowManager gameFlowManager;

    public TextMeshProUGUI[] topicTexts;
    public TextMeshPro[] votingButtonTexts;
    public Image[] topicPanels;
    public string[] wordDatabase;
    public Color normalTextColor = Color.white;
    public Color answerTextColor = Color.red;

    [UdonSynced] private int[] currentWordIndices = new int[5];
    [UdonSynced] private int correctIndex = -1;

    [Header("UI Text References")]
    public TextMeshProUGUI correctTopicText;

    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        UpdateTopicUI();
    }

    public override void OnDeserialization()
    {
        UpdateTopicUI();
        if (gameFlowManager != null)
        {
            bool amIParent = (Networking.LocalPlayer.playerId == gameFlowManager.currentParentId);
            HighlightAnswerForParent(amIParent);
        }
    }

    public void _ApplyTopicHighlight(bool shouldHighlight)
    {
        for (int i = 0; i < 5; i++)
        {
            if (i < topicTexts.Length && topicTexts[i] != null)
            {
                // 自分が親で、かつそのインデックスが正解なら赤、それ以外は白
                if (shouldHighlight && i == correctIndex)
                {
                    topicTexts[i].color = answerTextColor;
                }
                else
                {
                    topicTexts[i].color = normalTextColor;
                }
            }
        }
    }


    public void DrawNewTopics()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(localPlayer, gameObject);
        if (wordDatabase.Length < 5) return;
        int count = 0;
        for (int i = 0; i < 5; i++) currentWordIndices[i] = -1;
        while (count < 5)
        {
            int candidate = Random.Range(0, wordDatabase.Length);
            bool isDuplicate = false;
            for (int i = 0; i < count; i++) { if (currentWordIndices[i] == candidate) { isDuplicate = true; break; } }
            if (!isDuplicate) { currentWordIndices[count] = candidate; count++; }
        }
        correctIndex = Random.Range(0, 5);
        RequestSerialization();
        UpdateTopicUI();
    }

    public void UpdateTopicUI()
    {
        for (int i = 0; i < 5; i++)
        {
            string word = "---";
            int wordID = currentWordIndices[i];
            if (wordID >= 0 && wordID < wordDatabase.Length) word = wordDatabase[wordID];

            if (i < topicTexts.Length && topicTexts[i] != null)
            {
                topicTexts[i].text = word;
                // 色のリセット（ハイライトは別のメソッドで行う）
                topicTexts[i].color = normalTextColor;
            }
            if (i < votingButtonTexts.Length && votingButtonTexts[i] != null)
            {
                votingButtonTexts[i].text = word;
            }
        }
    }

    public void HighlightAnswerForParent(bool amIParent)
    {
        _ApplyTopicHighlight(amIParent);
    }

    // --- 回答受付 ---

    public void OnSubmitAnswer(int index)
    {
        if (gameFlowManager != null)
        {
            if (localPlayer.playerId != gameFlowManager.currentGuesserId)
            {
                return;
            }
        }

        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, "CheckAnswer_" + index);
    }

    public void CheckAnswer_0() { CheckAnswerLogic(0); }
    public void CheckAnswer_1() { CheckAnswerLogic(1); }
    public void CheckAnswer_2() { CheckAnswerLogic(2); }
    public void CheckAnswer_3() { CheckAnswerLogic(3); }
    public void CheckAnswer_4() { CheckAnswerLogic(4); }

    private void CheckAnswerLogic(int index)
    {
        if (!gameFlowManager.isThinkingPhase) return;
        bool isCorrect = (index == correctIndex);
        if (gameFlowManager != null) gameFlowManager.OnAnswerResult(isCorrect);
    }
}