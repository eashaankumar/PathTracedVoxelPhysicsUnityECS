using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ScaleBufferGridLayout : MonoBehaviour
{
    [SerializeField]
    GridLayoutGroup group;
    [SerializeField]
    int numCellsWidth;
    [SerializeField]
    Vector2Int targetRes;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float ratio = (float)targetRes.y / targetRes.x;
        int width = Screen.width / numCellsWidth;
        int height = (int)(width / ratio);
        group.cellSize = new Vector2(width, height);
    }
}
