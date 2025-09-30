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
    [SerializeField] private ObstacleController obstacleController;
    [SerializeField] private float moveSpeed = 10f;

    [Header("-------------- Player Animation Variables --------------")]
    [SerializeField] private Animator animator;

    [Header("Invulnerability")]
    [SerializeField] private float invulnerabilityDuration = 1.5f;
    [SerializeField] private bool isInvulnerable = false;
    [SerializeField] private bool canBeInvulnerable = false;

    [Header("-------------- Attack Variables --------------")]
    [SerializeField] private Transform enemyParent; // Drag Enemy Parent GameObject here

    [Header("UI Controller")]
    [SerializeField] private UIController uiController;

    private float invulnerabilityTimer = 0f;
    private Vector3 targetPosition;
    private bool isMoving = false;
    private bool player_died = false;
    private bool deathAnimationComplete = false;
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

        // Will be true if shift is held down
        // Used for attack action
        bool isAttack = Input.GetKey(KeyCode.LeftShift);

        // Only allow movement in one direction at a time
        if (Input.GetKeyDown(KeyCode.W)) direction = Vector3.up;
        else if (Input.GetKeyDown(KeyCode.S)) direction = Vector3.down;
        else if (Input.GetKeyDown(KeyCode.A)) direction = Vector3.left;
        else if (Input.GetKeyDown(KeyCode.D)) direction = Vector3.right;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if(canBeInvulnerable)
            {
                if (!isInvulnerable)
                {
                    BecomeInvulnerable();
                }
            }
        }

        // If a direction was chosen, calculate target position
        if (direction != Vector3.zero)
        {

            Vector3Int currentGrid = tilemap.WorldToCell(transform.position);
            Vector3Int targetGrid = currentGrid + Vector3Int.RoundToInt(direction);

            if (isAttack)
            {
                // Attack mode: don't move, just face direction and attack
                // Handle facing direction
                if (direction.x > 0) // Facing right
                    transform.localScale = new Vector3(1, 1, 1);
                else if (direction.x < 0) // Facing left
                    transform.localScale = new Vector3(-1, 1, 1);

                // Trigger attack animation
                animator.SetTrigger("Attack");

                // Check for enemies in attack direction
                CheckAttackHit(targetGrid);
            }
            else if (IsValidPosition(targetGrid))  // Check tilemap bounds
            {
                targetPosition = tilemap.GetCellCenterWorld(targetGrid);
                isMoving = true;

                // Handle facing direction
                if (direction.x > 0) // Moving right
                    transform.localScale = new Vector3(1, 1, 1);
                else if (direction.x < 0) // Moving left
                    transform.localScale = new Vector3(-1, 1, 1);

                // Trigger movement animation
                animator.SetTrigger("Dash");
            }
        }
    }

    void CheckAttackHit(Vector3Int attackGridPosition)
    {
        // Convert attack position to world position for comparison
        Vector3 attackWorldPos = tilemap.GetCellCenterWorld(attackGridPosition);
        Debug.Log($"Attack world position: {attackWorldPos}");

        if (enemyParent == null)
        {
            Debug.LogError("Enemy parent is null! Make sure to assign it in the inspector.");
            return;
        }

        // Check each enemy child
        for (int i = 0; i < enemyParent.childCount; i++)
        {
            Transform enemy = enemyParent.GetChild(i);

            // Convert enemy position to grid position
            Vector3Int enemyGridPos = tilemap.WorldToCell(enemy.position);

            // Check if enemy is at the attack position
            if (enemyGridPos == attackGridPosition)
            {
                // Get the enemy component and damage it
                var enemyScript = enemy.GetComponent<EnemyScript>(); // Replace with your actual enemy script name
                if (enemyScript != null)
                {
                    enemyScript.TakeDamage();
                }
                else
                {
                    Debug.LogError($"Enemy at {attackGridPosition} doesn't have EnemyScript component!");
                }

                break;
            }
        }
    }

    bool IsValidPosition(Vector3Int gridPosition)
    {
        // Simple bounds check
        BoundsInt bounds = tilemap.cellBounds;

        // Check for obstacles
        return obstacleController.CanMoveTo(tilemap.GetCellCenterWorld(gridPosition)) && bounds.Contains(gridPosition);
    }

    // Method to apply damage to the player
    public void TakeDamage(int damage)
    {
        if (isInvulnerable || player_died) return;

        playerHealth -= damage;

        if(playerHealth >=0 ) uiController.UpdateHealthUI(playerHealth);

        if (playerHealth <= 0)
        {
            Debug.Log("Player has died.");

            // Handle player death
            StartCoroutine(PlayDeathAnimation());

            // Set player_died to true to prevent further actions
            player_died = true;
        }
        else
        {

            // Start invulnerability
            isInvulnerable = true;
            invulnerabilityTimer = invulnerabilityDuration;

            // Change sprite color during invulnerability (When Added)
            if (playerSprite != null)
                playerSprite.color = new Color(1f, 1f, 1f, 0.5f);

            Debug.Log("Player took damage, now invulnerable for " + invulnerabilityDuration + " seconds");
        }
    }
    private void BecomeInvulnerable()
    {
        // Start invulnerability
        isInvulnerable = true;
        invulnerabilityTimer = invulnerabilityDuration;

        // Change sprite color during invulnerability (When Added)
        if (playerSprite != null)
            playerSprite.color = new Color(1f, 1f, 1f, 0.5f);

        Debug.Log("Player took damage, now invulnerable for " + invulnerabilityDuration + " seconds");
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

    public void OnDeathAnimationComplete()
    {
        deathAnimationComplete = true;
    }

    private IEnumerator PlayDeathAnimation()
    {
        // Start death animation
        animator.SetTrigger("Death");

        // Wait until animation is complete, Animator will call OnDeathAnimationComplete
        yield return new WaitUntil(() => deathAnimationComplete);

        // Disable player object
        gameObject.SetActive(false);                            
    }
}
