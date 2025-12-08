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

    [Header("UI Components")]
    public TextMeshProUGUI[] topicTexts;
    public Image[] topicPanels;

    [Header("Data")]
    public string[] wordDatabase;

    [Header("Colors")]
    public Color normalTextColor = Color.white;
    public Color answerTextColor = Color.red;

    // 同期変数
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

    public void DrawNewTopics()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(localPlayer, gameObject);
        }

        if (wordDatabase.Length < 5)
        {
            Debug.LogError("Not enough words in database! Need at least 5.");
            return;
        }

        // ▼▼▼ 修正: 重複なし抽選ロジック ▼▼▼
        int count = 0;
        // 初期化（-1にしておく）
        for (int i = 0; i < 5; i++) currentWordIndices[i] = -1;

        while (count < 5)
        {
            // ランダムに選ぶ
            int candidate = Random.Range(0, wordDatabase.Length);

            // 既に選ばれていないかチェック
            bool isDuplicate = false;
            for (int i = 0; i < count; i++)
            {
                if (currentWordIndices[i] == candidate)
                {
                    isDuplicate = true;
                    break;
                }
            }

            // 被ってなければ採用
            if (!isDuplicate)
            {
                currentWordIndices[count] = candidate;
                count++;
            }
        }
        // ▲▲▲ ▲▲▲

        correctIndex = Random.Range(0, 5);

        RequestSerialization();
        UpdateTopicUI();
    }

    public void UpdateTopicUI()
    {
        for (int i = 0; i < 5; i++)
        {
            if (i < topicTexts.Length && topicTexts[i] != null)
            {
                int wordID = currentWordIndices[i];
                if (wordID >= 0 && wordID < wordDatabase.Length)
                {
                    topicTexts[i].text = wordDatabase[wordID];
                }
                else
                {
                    topicTexts[i].text = "---";
                }
                topicTexts[i].color = normalTextColor;
            }
        }
    }

    public void HighlightAnswerForParent(bool amIParent)
    {
        for (int i = 0; i < 5; i++)
        {
            if (i < topicTexts.Length && topicTexts[i] != null)
            {
                if (amIParent && i == correctIndex)
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
}