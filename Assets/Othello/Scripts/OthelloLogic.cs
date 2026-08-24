using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

        bool isPlayable = false; // 石を置けるかどうかの判定結果の例


        // 例　右側に相手の石があるとき、自分の石を置くことができると判定する場合（暴論）
        if(x < 7 && StoneData[x+1][z] == team*-1) // 右側が相手の石であれば、
        {
            isPlayable = true; // 石を置けると判定する
        }

        // -------------------------------------------------------------------




        return isPlayable;
    }
}