using TMPro;
using UnityEngine;


public class Collectible : MonoBehaviour
{
    public int collectibleCount;
    public TMP_Text collectibleText;
    public GameObject door;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (collectibleText != null)
        {
            collectibleText.text = "Collectible Count: " + collectibleCount.ToString();

        }
    }
}
