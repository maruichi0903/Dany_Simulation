using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectPoolManager : UdonSharpBehaviour
{
    [Tooltip("プールする全種類のオブジェクトプレハブ")]
    public GameObject[] objectPrefabs;

    [Tooltip("各種類ごとにプールする個数")]
    public int poolSizePerType = 10;

    // プール配列 ( [種類ID][個数インデックス] )
    private GameObject[][] objectPools;
    private int[] currentPoolIndex;

    void Start()
    {
        InitializePools();
    }

    private void InitializePools()
    {
        if (objectPrefabs == null || objectPrefabs.Length == 0) return;

        int typeCount = objectPrefabs.Length;

        // 配列の初期化
        objectPools = new GameObject[typeCount][];
        currentPoolIndex = new int[typeCount];

        // 各Prefabごとに生成してプールに格納
        for (int i = 0; i < typeCount; i++)
        {
            objectPools[i] = new GameObject[poolSizePerType];
            currentPoolIndex[i] = 0;

            GameObject prefab = objectPrefabs[i];
            if (prefab == null) continue;

            for (int j = 0; j < poolSizePerType; j++)
            {
                GameObject newObj = Instantiate(prefab);
                newObj.SetActive(false);
                objectPools[i][j] = newObj;
            }
        }
        Debug.Log("[ObjectPoolManager] Pools initialized.");
    }

    /// <summary>
    /// 指定したIDのオブジェクトをプールから取り出す
    /// </summary>
    public GameObject GetNextBlock(int objectID)
    {
        // IDの範囲チェック
        if (objectPools == null || objectID < 0 || objectID >= objectPools.Length)
        {
            Debug.LogError("[ObjectPoolManager] Invalid object ID: " + objectID);
            return null;
        }

        // プールから取得
        GameObject[] pool = objectPools[objectID];
        int index = currentPoolIndex[objectID];
        GameObject obj = pool[index];

        // インデックスを進める（循環）
        currentPoolIndex[objectID] = (index + 1) % poolSizePerType;

        // 万が一アクティブなままなら一度リセット（位置移動時のチラつき防止など）
        if (obj.activeSelf)
        {
            obj.SetActive(false);
        }

        return obj;
    }
}
