using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class PlayerController : MonoBehaviour
{
    [Header("-------------- Player Variables --------------")]
    [SerializeField] private int playerHealth;
    [SerializeField] private SpriteRenderer playerSprite;

    [Header("-------------- Player Movement Variables --------------")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private float moveSpeed = 10f;

    [Header("Invulnerability")]
    [SerializeField] private float invulnerabilityDuration = 1.5f;
    [SerializeField] private bool isInvulnerable = false;

    private float invulnerabilityTimer = 0f;
    private Vector3 targetPosition;
    private bool isMoving = false;

    void Start()
    {
        // Snap to grid on start
        Vector3Int gridPos = tilemap.WorldToCell(transform.position);
        targetPosition = tilemap.GetCellCenterWorld(gridPos);
        transform.position = targetPosition;
    }

    void Update()
    {
        // Only move if not already moving
        if (isMoving)
        {
            // Move towards target position
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

            // Check when larped to target
            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition;
                isMoving = false;
            }
        }
        else
        {
            // Handle input for movement
            HandleInput();
        }

    }

    void HandleInput()
    {
        Vector3 direction = Vector3.zero;

        // Only allow movement in one direction at a time
        if (Input.GetKeyDown(KeyCode.W)) direction = Vector3.up;
        else if (Input.GetKeyDown(KeyCode.S)) direction = Vector3.down;
        else if (Input.GetKeyDown(KeyCode.A)) direction = Vector3.left;
        else if (Input.GetKeyDown(KeyCode.D)) direction = Vector3.right;

        // If a direction was chosen, calculate target position
        if (direction != Vector3.zero)
        {
            Vector3Int currentGrid = tilemap.WorldToCell(transform.position);
            Vector3Int targetGrid = currentGrid + Vector3Int.RoundToInt(direction);

            // Check tilemap bounds
            if (IsValidPosition(targetGrid))
            {
                targetPosition = tilemap.GetCellCenterWorld(targetGrid);
                isMoving = true;
            }
        }
    }

    bool IsValidPosition(Vector3Int gridPosition)
    {
        // Simple bounds check
        BoundsInt bounds = tilemap.cellBounds;
        return bounds.Contains(gridPosition);
    }

    // Method to apply damage to the player
    public void TakeDamage(int damage)
    {
        if (isInvulnerable) return;

        
        if (playerHealth <= 0)
        {
            Debug.Log("Player has died.");
            // Handle player death (e.g., restart level, show game over screen)
            gameObject.SetActive(false); // Simple way to "remove" player
        }
        else
        {
            playerHealth -= damage;

            // Start invulnerability
            isInvulnerable = true;
            invulnerabilityTimer = invulnerabilityDuration;

            // Change sprite color during invulnerability (When Added)
            if (playerSprite != null)
                playerSprite.color = new Color(1f, 1f, 1f, 0.5f);

            Debug.Log("Player took damage, now invulnerable for " + invulnerabilityDuration + " seconds");
        }
    }

    public void TickInvulnerability()
    {
        if (isInvulnerable)
        {
            invulnerabilityTimer -= 1;
            Debug.Log("Invulnerability time left: " + invulnerabilityTimer);
            if (invulnerabilityTimer < 0)
            {
                isInvulnerable = false;
                // Reset sprite color if using visual feedback
                if (playerSprite != null)
                    playerSprite.color = Color.white;
            }
        }
    }

    public bool IsInvulnerable()
    {
        return isInvulnerable;
    }
}
