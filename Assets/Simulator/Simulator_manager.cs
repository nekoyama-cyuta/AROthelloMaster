using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class StoneRow
{
    public GameObject[] row;
}

public class Simulator_manager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject bordBlock_prefab;
    [SerializeField] private GameObject stone_prefab;
    [SerializeField] private GameObject playableStone_prefab;

    [Header("Visuals")]
    [SerializeField] private StoneRow[] stones;
    [SerializeField] private List<GameObject> playableStones;

    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeStones();
        Generate();

        UpdatePlayableStones(1); // 白石のターンのときに置けるマスを表示する
    }

    void InitializeStones()
    {
        stones = new StoneRow[8];
        for(int i=0; i<8; i++)
        {
            stones[i] = new StoneRow();
            stones[i].row = new GameObject[8];
        }
    }

    public void Generate()
    {
        for(int i=0; i<8; i++)
        {
            for(int j=0; j<8; j++)
            {
                Instantiate(bordBlock_prefab, new Vector3(i, 0, j), Quaternion.identity);
            }
        }

        UpdateAllStones();
    }

    public void UpdateAllStones()
    {
        for(int i=0; i<8; i++)
        {
            for(int j=0; j<8; j++)
            {
                UpdateStone(i, j, DataLayer.StoneData[i][j]);
            }
        }
    }

    public void UpdateStone(int x, int y, int stoneType)
    {
        if(stoneType == 0)
        {
            if(stones[x].row[y] != null)
            {
                Destroy(stones[x].row[y]);
                stones[x].row[y] = null;
            }
            return;
        }

        GameObject stone = stones[x].row[y];
        if(stone == null)
        {
            stone = Instantiate(stone_prefab, new Vector3(x, 0.1f, y), Quaternion.identity);
            stones[x].row[y] = stone;
        }

        stones[x].row[y].transform.localScale = new Vector3(1, stoneType, 1);
    }

    public void UpdatePlayableStones(int team)
    {
        // すでに表示されている置けるマスのオブジェクトを削除する
        for(int i=0; i<playableStones.Count; i++)
        {
            if(playableStones[i] != null)
            {
                Destroy(playableStones[i]);
            }
        }
        playableStones = new List<GameObject>();


        List<Vector2Int> playablePositions = OthelloLogic.Instance.playableStonePositions(team);
        for(int i=0; i<playablePositions.Count; i++)
        {
            Debug.Log("Playable Position: " + playablePositions[i]);
            Vector2Int pos = playablePositions[i];
            playableStones.Add(Instantiate(playableStone_prefab, new Vector3(pos.x, 0.1f, pos.y), Quaternion.identity));
        }
    }
}
