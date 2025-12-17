using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectPoolManager : UdonSharpBehaviour
{
    public GameObject[] objectPrefabs;
    public int poolSizePerType = 10;
    public GameObject[][] objectPools;
    private int[] currentPoolIndex;

    void Start()
    {
        InitializePools();
    }

    private void InitializePools()
    {
        if (objectPrefabs == null || objectPrefabs.Length == 0) return;
        int typeCount = objectPrefabs.Length;

        objectPools = new GameObject[typeCount][];
        currentPoolIndex = new int[typeCount];

        for (int i = 0; i < typeCount; i++)
        {
            objectPools[i] = new GameObject[poolSizePerType];
            currentPoolIndex[i] = 0;
            GameObject prefab = objectPrefabs[i];
            if (prefab == null) continue;

            for (int j = 0; j < poolSizePerType; j++)
            {
                GameObject newObj = Instantiate(prefab);
                newObj.SetActive(true);

                // 1. 常に Active(true) にする
                newObj.SetActive(true);

                // 2. 物理演算を止める
                Rigidbody rb = newObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                // 3. GameBlockの機能で「非表示状態」として初期化する
                GameBlock block = newObj.GetComponent<GameBlock>();
                if (block != null)
                {
                    // ここで強制的に変数をfalseにして見た目を更新
                    // （Start時に自動で呼ばれるが、念のため明示的にやる）
                    block.ResetBlock();
                }
                else
                {
                    newObj.SetActive(false);
                }

                objectPools[i][j] = newObj;
            }
        }
        Debug.Log("[ObjectPoolManager] Pools initialized.");
    }

    public GameObject GetNextBlock(int objectID)
    {
        if (objectPools == null || objectID < 0 || objectID >= objectPools.Length) return null;

        GameObject[] pool = objectPools[objectID];
        int index = currentPoolIndex[objectID];
        GameObject obj = pool[index];

        currentPoolIndex[objectID] = (index + 1) % poolSizePerType;

        GameBlock block = obj.GetComponent<GameBlock>();
        if (block != null && block.isOccupied)
        {
            block.ResetBlock();
        }

        return obj;
    }
}