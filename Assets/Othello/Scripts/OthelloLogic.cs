using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Mono.Cecil.Cil;
using Unity.VisualScripting;
using UnityEngine.Analytics;

public class OthelloLogic : MonoBehaviour
{
    public static OthelloLogic Instance { get; private set; }
    private void Awake() => Instance = this;

    // 置けるマス一覧を返す関数
    public List<Vector2Int> playableStonePositions(int team)
    {
        List<Vector2Int> playablePositions = new List<Vector2Int>();

        for(int x=0; x<8; x++)
        {
            for(int z=0; z<8; z++)
            {
                if(DataLayer.StoneData[x][z] != 0) continue; // すでに石が置かれているマスはスキップ
                if(isPlayableStone(team, x, z))
                {
                    playablePositions.Add(new Vector2Int(x, z));
                }
            }
        }        
        return playablePositions;
    }
    
    // 座標x,zに石を置けるかどうかを判定する関数
    // team: -1=黒石、1=白石
    public bool isPlayableStone(int team, int x, int z)
    {
        int[][] StoneData = DataLayer.StoneData; // 今の盤面の状態



        // 石を置けるかを計算する処理をここに実装して！！！！！------------------

        if(StoneData[x][z] != 0) return false;
        int opponent = team*-1;
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(0,1),
            new Vector2Int(0,-1),
            new Vector2Int(-1,0),
            new Vector2Int(1,0),
            new Vector2Int(-1,1),
            new Vector2Int(1,1),
            new Vector2Int(-1,-1),
            new Vector2Int(1,-1)
        };
        foreach(var dir in directions)
        {
            int checkX = x+dir.x;
            int checkZ = z+dir.y;

            if(checkX < 0 || checkX >= 8 || checkZ < 0 || checkZ >= 8) continue;
            if(StoneData[checkX][checkZ] != opponent) continue;
            while(true)
            {
                checkX += dir.x;
                checkZ += dir.y;
                if(checkX < 0 || checkX >= 8 || checkZ < 0 || checkZ >= 8) break;
                if(StoneData[checkX][checkZ] == 0) break;
                if(StoneData[checkX][checkZ] == team)
                {
                    return true;
                }
            }
        }
        return false;
        // -------------------------------------------------------------------
    }
}