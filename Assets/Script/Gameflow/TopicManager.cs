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

    // ... (他の変数はそのまま) ...
    public TextMeshProUGUI[] topicTexts;
    public TextMeshPro[] votingButtonTexts;
    public Image[] topicPanels;
    public string[] wordDatabase;
    public Color normalTextColor = Color.white;
    public Color answerTextColor = Color.red;

    [UdonSynced] private int[] currentWordIndices = new int[5];
    [UdonSynced] private int correctIndex = -1;

    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        UpdateTopicUI();
    }

    public override void OnDeserialization()
    {
        UpdateTopicUI();
    }

    // ... (DrawNewTopics, UpdateTopicUI, HighlightAnswerForParent はそのまま) ...
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
            if (i < topicTexts.Length && topicTexts[i] != null) { topicTexts[i].text = word; topicTexts[i].color = normalTextColor; }
            if (i < votingButtonTexts.Length && votingButtonTexts[i] != null) votingButtonTexts[i].text = word;
        }
    }

    public void HighlightAnswerForParent(bool amIParent)
    {
        for (int i = 0; i < 5; i++)
        {
            if (i < topicTexts.Length && topicTexts[i] != null)
            {
                if (amIParent && i == correctIndex) topicTexts[i].color = answerTextColor;
                else topicTexts[i].color = normalTextColor;
            }
        }
    }

    // --- 回答受付 ---

    public void OnSubmitAnswer(int index)
    {
        // ▼▼▼ 追加: 回答者（Guesser）本人でなければ無視！ ▼▼▼
        if (gameFlowManager != null)
        {
            // 自分が回答者じゃないなら、ここで終了
            if (localPlayer.playerId != gameFlowManager.currentGuesserId)
            {
                // 必要なら「あなたは回答者ではありません」等の音を鳴らしてもよい
                return;
            }
        }
        // ▲▲▲ ▲▲▲

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