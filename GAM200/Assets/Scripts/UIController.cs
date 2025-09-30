using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    [Header("UI Elements")]
    [Header("Health UI")]
    [SerializeField] private GameObject HealthUI;
    [SerializeField] private Texture FullHeartSprite;
    [SerializeField] private Texture EmptyHeartSprite;

    // Health UI
    private List<RawImage> healthUISpriteRenderers = new List<RawImage>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Take all SpriteRenderers from HealthUI
        foreach (RawImage ri in HealthUI.GetComponentsInChildren<RawImage>())
        {
            // Null check
            if (ri == null) continue;

            ri.texture = FullHeartSprite; // Set to full heart

            // Add to array
            healthUISpriteRenderers.Add(ri);
        }
    }

    public void UpdateHealthUI(int currentHealth)
    {
        // Loop through all hearts
        for (int i = 0; i < healthUISpriteRenderers.Count; i++)
        {
            if (i < currentHealth)
            {
                // Full heart
                healthUISpriteRenderers[i].texture = FullHeartSprite;
            }
            else
            {
                // Empty heart
                healthUISpriteRenderers[i].texture = EmptyHeartSprite;
            }
        }
    }
}