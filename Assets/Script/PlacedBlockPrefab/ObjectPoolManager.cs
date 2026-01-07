using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectPoolManager : UdonSharpBehaviour
{
    [Header("ここにPrefabを全種類登録する（名前照合用）")]
    public GameObject[] objectPrefabs; // ← 元々あったこの配列は復活させます！

    [Header("ここにシーン上の全ての複製ブロックを登録する")]
    public GameBlock[] scenePoolObjects; // ← ここにCapsuleもCastleも全部まとめて放り込む！

    void Start()
    {
        InitializePools();
    }

    private void InitializePools()
    {
        if (scenePoolObjects == null) return;

        foreach (GameBlock block in scenePoolObjects)
        {
            if (block != null)
            {
                // 最初はすべてリセット（非表示）
                block.ResetBlock();
            }
        }
        Debug.Log("[ObjectPoolManager] Pools initialized.");
    }

    public GameObject GetNextBlock(int objectID)
    {
        if (objectPrefabs == null || objectID < 0 || objectID >= objectPrefabs.Length) return null;
        if (scenePoolObjects == null) return null;

        // 探したいPrefabの元々の名前（例: "Cube"）
        string targetName = objectPrefabs[objectID].name;

        foreach (GameBlock block in scenePoolObjects)
        {
            if (block == null) continue;

            string blockName = block.gameObject.name;

            // ★修正ポイント：判定ロジックを改良しました
            // 1. 完全一致（"Cube"）
            // 2. 複製時の番号付き（"Cube (1)"） ※ここにスペースを入れた！
            bool isMatch = (blockName == targetName) ||
                           blockName.StartsWith(targetName + " (");

            if (isMatch)
            {
                if (!block.isOccupied)
                {
                    GameObject obj = block.gameObject;
                    if (!Networking.IsOwner(obj)) Networking.SetOwner(Networking.LocalPlayer, obj);
                    return obj;
                }
            }
        }

        // ここがログに出ている警告の場所です
        Debug.LogWarning($"[ObjectPoolManager] No free object found for ID {objectID} ({targetName})");
        return null;
    }
}