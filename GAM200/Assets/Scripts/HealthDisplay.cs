using UnityEngine;
using UnityEngine.UI;

public class HealthDisplay : MonoBehaviour
{
    public GameObject[] hearts;

    public void UpdateHUD(int currentHP)
    {
        for(int i = 0; i < hearts.Length;i++)
        {
            hearts[i].SetActive(i < currentHP);
        }
    }
}
