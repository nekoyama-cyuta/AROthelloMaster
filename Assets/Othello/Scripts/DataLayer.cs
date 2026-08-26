using UnityEngine;

public class DataLayer : MonoBehaviour
{
    // 0: 置かれていない、 -1: 黒石、1: 白石
    public static int[][] StoneData = new int[8][]
    {
        new int[8]{0,0,0,0,1,-1,-1,0},
        new int[8]{0,0,0,0,1,0,0,0},
        new int[8]{0,0,0,-1,0,0,0,0},
        new int[8]{0,0,0,-1,1,0,0,0},
        new int[8]{0,0,-1,1,-1,-1,0,0},
        new int[8]{0,0,0,0,0,1,0,0},
        new int[8]{0,0,0,0,0,0,0,0},
        new int[8]{0,0,0,0,0,-1,0,0}
    };
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
