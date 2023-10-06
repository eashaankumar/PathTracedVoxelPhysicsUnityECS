using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    [SerializeField]
    TMP_Text text;

    float current;
    int frames;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        current += Time.deltaTime;
        if (current >= 1)
        {
            text.text = frames + "";
            frames = 0;
            current = 0;
        }
        frames++;
    }
}
