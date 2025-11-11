/*
* Copyright (c) 2025 Infernumb. All rights reserved.
*
* This code is part of the hellmaker prototype project.
* Unauthorized copying of this file, via any medium, is strictly prohibited.
* Proprietary and confidential.
*
* Author:
*   - Lin Xin
*   - Contribution Level (100%)
* @file
* @brief
*   Beat-synced turret that automatically fires BeatProjectile prefabs.
*   - Subscribes to BeatBus and shoots every N beats with an optional offset.
*   - Configures projectile movement, damage, and lifetime via BeatProjectile.Config.
*   - Allows independent control of shoot direction (logic) and prefab rotation (visual).
*   - Auto-finds references to PlayerController and Tilemap if not set.
*/

using UnityEngine;
using UnityEngine.Tilemaps;

public class TurretShooter : MonoBehaviour
{
    // Cardinal directions for both movement and visual prefab rotation.
    public enum CardinalDir { Right, Left, Up, Down }

    [Header("Beat Scheduling")]
    [SerializeField] int fireEveryN = 6;  // Fire once every N beats.
    [SerializeField] int fireOffset = 0;  // Offset to desync with other turrets.

    [Header("Projectile")]
    [SerializeField] GameObject projectilePrefab;     // Prefab to instantiate (must have BeatProjectile).
    [SerializeField] Transform muzzle;                // Optional specific spawn point.
    [SerializeField] int projectileDamage = 1;
    [SerializeField] int projectileMaxSteps = 12;
    [SerializeField] float projectileStepDistance = 1f;

    [Header("Direction (Movement)")]
    [SerializeField] CardinalDir shootDirection = CardinalDir.Right; // Which way the projectile travels.

    [Header("Rotation (Visual Prefab)")]
    [SerializeField] CardinalDir prefabRotation = CardinalDir.Right; // How the prefab sprite is rotated visually.

    [Header("Collisions")]
    [SerializeField] PlayerController player; // Target player reference for projectile damage.
    [SerializeField] Tilemap gridTilemap;     // Tilemap used for grid snapping.

    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    [SerializeField] private Animator animator;

    int _beatCount = -1; // Tracks current beat index.

    void Awake()
    {
        // Auto-assign references if not manually set.
        if (!player) player = FindOne<PlayerController>();
        if (!gridTilemap) gridTilemap = FindOne<Tilemap>();
    }

    // Subscribe/unsubscribe to beat events when enabled/disabled.
    void OnEnable() => BeatBus.Beat += OnBeat;
    void OnDisable() => BeatBus.Beat -= OnBeat;

    /// <summary>
    /// Called every beat from BeatBus. Checks if this beat is a scheduled fire tick.
    /// </summary>
    void OnBeat(int beatIndex)
    {
        _beatCount = beatIndex;
        if ((_beatCount - fireOffset) % Mathf.Max(1, fireEveryN) == 0)
            FireOnce();
    }

    /// <summary>
    /// Spawns one projectile, configures it, and initializes its movement + damage.
    /// </summary>
    void FireOnce()
    {
        // Safety: require prefab, player, and tilemap references.
        if (!projectilePrefab || !player || !gridTilemap)
        {
            if (debugLogs)
                Debug.LogWarning("[TurretShooter] Missing prefab/player/tilemap reference.");
            return;
        }

        // Decide projectile movement direction.
        Vector2 dir = Vector2.right;
        switch (shootDirection)
        {
            case CardinalDir.Right: dir = Vector2.right; break;
            case CardinalDir.Left:  dir = Vector2.left;  break;
            case CardinalDir.Up:    dir = Vector2.up;    break;
            case CardinalDir.Down:  dir = Vector2.down;  break;
        }

        // Decide prefab’s visual rotation (independent from movement).
        float zDeg = 0f;
        switch (prefabRotation)
        {
            case CardinalDir.Up:    zDeg = 0f;   break;
            case CardinalDir.Right: zDeg = 90f;  break;
            case CardinalDir.Down:  zDeg = 180f; break;
            case CardinalDir.Left:  zDeg = -90f; break;
        }

        // Spawn position and rotation.
        Vector3 spawnPos = muzzle ? muzzle.position : transform.position;
        Quaternion spawnRot = Quaternion.Euler(0f, 0f, zDeg);

        // Instantiate projectile prefab.
        var go = Instantiate(projectilePrefab, spawnPos, spawnRot);

        // Ensure it has a BeatProjectile component.
        var proj = go.GetComponent<BeatProjectile>();
        if (!proj) { Destroy(go); return; }

        // Build config struct for projectile.
        var cfg = new BeatProjectile.Config
        {
            direction = dir,
            stepDistance = projectileStepDistance,
            snapToGrid = true,
            smoothStep = true,
            moveLerpFractionOfInterval = 0.35f,
            bpmForSmoothing = 120,
            intervalBeatsForSmoothing = 1f,
            gridTilemap = gridTilemap,
            maxSteps = projectileMaxSteps,
            damage = projectileDamage,
            player = player
        };

        // Initialize projectile with config.
        proj.Initialize(cfg);

        if (debugLogs)
            Debug.Log($"[TurretShooter] Fired beat={_beatCount}, dir={dir}, prefabRot={prefabRotation} ({zDeg}°)");
    }

    /// <summary>
    /// Unity-version-safe way to find one object of type T.
    /// </summary>
    static T FindOne<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER || UNITY_2022_3_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
