using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    [Header("-------------- Attack Variables --------------")]
    [SerializeField] private Transform enemyParent; // Drag Enemy Parent GameObject here

    [Header("UI Controller")]
    [SerializeField] private UIController uiController;

    [Header("-------------- Shield System --------------")]
    [SerializeField] private bool hasShieldAbility = false;  // player starts without the shield power
    [SerializeField] public bool isShieldActive = false;    // Current shield status
    [SerializeField] private GameObject shieldVisual;        // shield effect visual
    [SerializeField] private float shieldCooldown = 1f;      // cooldown after blocking
    private float shieldCooldownTimer = 0f;
    private bool shieldOnCooldown = false;

    private Vector3 moveStartPosition; // Where the movement started
    private float invulnerabilityTimer = 0f;
    private Vector3 targetPosition;
    private bool isMoving = false;
    private bool player_died = false;
    private bool deathAnimationComplete = false;
    public GameObject gameOverPanel;
    public Collectible diamond;
    private Vector3 direction = Vector3.zero;
    public bool isPowerup;
    private GameObject currentTeleporter;
    

    void Start()
    {
        // Check if there's a saved spawn position from RespawnManager
        if (RespawnManager.I != null)
        {
            Vector3 spawnPos = RespawnManager.I.GetSpawnOr(transform.position);
            transform.position = spawnPos;
            transform.rotation = RespawnManager.I.GetSpawnRotOr(transform.rotation);
        }

        // Snap to grid on start
        Vector3Int gridPos = tilemap.WorldToCell(transform.position);
        targetPosition = tilemap.GetCellCenterWorld(gridPos);
        transform.position = targetPosition;

        // Ensure shield visual starts hidden
        if (shieldVisual != null)
            shieldVisual.SetActive(false);
    }

    void Update()
    {
        // Only move if not already moving
        if (isMoving)
        {
            // Move towards target position
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition;
                isMoving = false;
            }

            // Check how far player has moved since start
            float distanceMoved = Vector3.Distance(moveStartPosition, transform.position);

            // If player moved 1f or more, stop and snap to tile center
            /*if (distanceMoved >= 1f)
            {
                Vector3Int gridCell = tilemap.WorldToCell(transform.position);
                transform.position = tilemap.GetCellCenterWorld(gridCell);

                isMoving = false;
            }*/
        }
        else
        {
            // Handle input for movement
            HandleInput();
        }

        if (player_died)
        {
            gameOverPanel.SetActive(true);
        }


        // Tick down invulnerability if needed
        TickInvulnerability();


        if (hasShieldAbility)
            HandleShieldCooldown();

        
    }

    void Teleporting()
    {
        if (currentTeleporter != null)
        {
            Debug.Log("Target location: " + targetPosition);
            transform.position = currentTeleporter.GetComponent<Teleporter>().GetDestination().position;
            isMoving = false;
            Debug.Log("Teleporter's position: " + currentTeleporter.GetComponent<Teleporter>().GetDestination().position);
            if (transform.position != null)
            {
                Debug.Log("Transform position: " + transform.position);
            }
            if (currentTeleporter != null)
            {
                Debug.Log("Current teleporter: " + currentTeleporter);
            }

        }
    }

    void HandleInput()
    {
        // Will be true if shift is held down
        // Used for attack action
        bool isAttack = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); 
        
        // Only allow movement in one direction at a time
        if (Input.GetKeyDown(KeyCode.W)) direction = Vector3.up;
        else if (Input.GetKeyDown(KeyCode.S)) direction = Vector3.down;
        else if (Input.GetKeyDown(KeyCode.A)) direction = Vector3.left;
        else if (Input.GetKeyDown(KeyCode.D)) direction = Vector3.right;
        else direction = Vector3.zero;

        // If a direction was chosen, calculate target position
        if (direction != Vector3.zero)
        {
            Debug.Log($"Input direction: {direction}");

            Vector3Int currentGrid = tilemap.WorldToCell(transform.position);
            Vector3Int targetGrid = currentGrid + Vector3Int.RoundToInt(direction);
            //moveStartPosition = transform.position; // Record where the move began
            //targetPosition = transform.position + direction; // Move 1f in that direction
            //isMoving = true;


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
            else if (IsValidPosition(targetGrid, direction))  // Check tilemap bounds
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
            Vector3 enemyGridPos = tilemap.WorldToCell(enemy.position);

            // Check if enemy is at the attack position
            if (enemyGridPos == attackGridPosition)
            {
                // Get the enemy component and damage it
                var enemyScript = enemy.GetComponent<EnemyScript>(); 
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


    bool IsValidPosition(Vector3Int gridPosition, Vector3 direction)
    {
        // Simple bounds check
        BoundsInt bounds = tilemap.cellBounds;

        // Check for obstacles
        //return obstacleController.CanMoveTo(tilemap.GetCellCenterWorld(gridPosition)) && bounds.Contains(gridPosition);
        return obstacleController.CanMoveTo(tilemap.GetCellCenterWorld(gridPosition), Vector3Int.RoundToInt(direction));

    }

    // Method to apply damage to the player
    public void TakeDamage(int damage)
    {
        if (player_died) return;

        // Shield absorbs one hit if active
        if (isShieldActive)
        {
            Debug.Log("Shield absorbed the damage!");
            DeactivateShield(); // consume shield charge
            return;
        }

        // If invulnerable from other effects, ignore
        if (isInvulnerable) return;

        // Apply damage
        playerHealth -= damage;
        if (playerHealth >= 0)
        {
            uiController.UpdateHealthUI(playerHealth);
        }

            if (playerHealth <= 0)
        {
            Debug.Log("Player has died.");
            StartCoroutine(PlayDeathAnimation());
            player_died = true;
        }
        else
        {
            // Normal invulnerability period
            isInvulnerable = true;
            invulnerabilityTimer = invulnerabilityDuration;

            /*if (playerSprite != null)
                playerSprite.color = new Color(1f, 1f, 1f, 0.5f);*/
            playerSprite.color = new Color(1f, 1f, 1f, 0.5f);
        }
    }

    public void TickInvulnerability()
    {
        if (isInvulnerable && !isShieldActive) // shield prevents countdown
        {
            invulnerabilityTimer -= Time.deltaTime;
            if (invulnerabilityTimer <= 0)
            {
                isInvulnerable = false;
                //if (playerSprite != null)
                    playerSprite.color = Color.white;
            }
        }
    }

    public void EnableShieldAbility()
    {
        if (!hasShieldAbility)
        {
            hasShieldAbility = true;
            ActivateShield();
            Debug.Log("Shield power-up collected! Shield activated.");
        }
    }

    private void ActivateShield()
    {
        if (!hasShieldAbility || shieldOnCooldown) return;

        isShieldActive = true;
        isInvulnerable = true;

        if (shieldVisual != null)
            shieldVisual.SetActive(true);

        playerSprite.color = new Color(1f, 1f, 1f, 0.5f);
        Debug.Log("Shield activated!");
    }

    // Deactivates shield after blocking a hit
    private void DeactivateShield()
    {
        isShieldActive = false;
        isInvulnerable = false;

        if (shieldVisual != null)
            shieldVisual.SetActive(false);

        playerSprite.color = Color.white;

        // Start cooldown
        shieldOnCooldown = true;
        shieldCooldownTimer = shieldCooldown;

        Debug.Log("Shield broke! Starting cooldown...");
    }

    // Handles cooldown logic — call from Update()
    private void HandleShieldCooldown()
    {
        if (shieldOnCooldown)
        {
            shieldCooldownTimer -= Time.deltaTime;

            if (shieldCooldownTimer <= 0f)
            {
                shieldOnCooldown = false;
                Debug.Log("Shield cooldown complete — reactivated!");
                ActivateShield();
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

    void OnTriggerEnter2D(Collider2D other)
    {
        /*if (other.gameObject.CompareTag("Collectible"))
        {
            Destroy(other.gameObject);
            diamond.collectibleCount++;
        }*/

        Debug.Log("Colliders are touching!");

        if (other.gameObject.CompareTag("Collectible"))
        {
            // Update collectible count
            if (diamond != null)
            {
                diamond.OnCollect(this); // Pass the PlayerController to grant shield ability
            }

            // Destroy the collectible object
            Destroy(other.gameObject);
        }

        if (other.CompareTag("Teleporter"))
        {
            currentTeleporter = other.gameObject;
            Teleporting();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Teleporter"))
        {
            if (other.gameObject == currentTeleporter)
            {
                currentTeleporter = null;
            }
        }
    }

    

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void RestartGamel()
    {
        SceneManager.LoadScene(2);
    }
    public void MainMenu()
    {
        SceneManager.LoadScene("Main Menu");
    }


}

