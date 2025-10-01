using UnityEngine;
using UnityEngine.Tilemaps;

public class TurretShooter : MonoBehaviour
{
    public enum CardinalDir { Right, Left, Up, Down }

    [Header("Beat Scheduling")]
    [SerializeField] int fireEveryN = 6;
    [SerializeField] int fireOffset = 0;

    [Header("Projectile")]
    [SerializeField] GameObject projectilePrefab;     // BeatProjectile prefab
    [SerializeField] Transform muzzle;                // optional spawn point
    [SerializeField] int projectileDamage = 1;
    [SerializeField] int projectileMaxSteps = 12;
    [SerializeField] float projectileStepDistance = 1f;

    [Header("Direction (Movement)")]
    [SerializeField] CardinalDir shootDirection = CardinalDir.Right;

    [Header("Rotation (Visual Prefab)")]
    [SerializeField] CardinalDir prefabRotation = CardinalDir.Right;

    [Header("Collisions")]
    [SerializeField] PlayerController player;
    [SerializeField] Tilemap gridTilemap;

    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    int _beatCount = -1;

    void Awake()
    {
        if (!player) player = FindOne<PlayerController>();
        if (!gridTilemap) gridTilemap = FindOne<Tilemap>();
    }

    void OnEnable() => BeatBus.Beat += OnBeat;
    void OnDisable() => BeatBus.Beat -= OnBeat;

    void OnBeat(int beatIndex)
    {
        _beatCount = beatIndex;
        if ((_beatCount - fireOffset) % Mathf.Max(1, fireEveryN) == 0)
            FireOnce();
    }

    void FireOnce()
    {
        if (!projectilePrefab || !player || !gridTilemap)
        {
            if (debugLogs)
                Debug.LogWarning("[TurretShooter] Missing prefab/player/tilemap reference.");
            return;
        }

        // Decide movement direction
        Vector2 dir = Vector2.right;
        switch (shootDirection)
        {
            case CardinalDir.Right: dir = Vector2.right; break;
            case CardinalDir.Left: dir = Vector2.left; break;
            case CardinalDir.Up: dir = Vector2.up; break;
            case CardinalDir.Down: dir = Vector2.down; break;
        }

        // Decide prefab rotation (independent of movement)
        float zDeg = 0f;
        switch (prefabRotation)
        {
            case CardinalDir.Right: zDeg = 0f; break;
            case CardinalDir.Up: zDeg = 90f; break;
            case CardinalDir.Left: zDeg = 180f; break;
            case CardinalDir.Down: zDeg = -90f; break;
        }

        Vector3 spawnPos = muzzle ? muzzle.position : transform.position;
        Quaternion spawnRot = Quaternion.Euler(0f, 0f, zDeg);

        // Instantiate projectile
        var go = Instantiate(projectilePrefab, spawnPos, spawnRot);

        var proj = go.GetComponent<BeatProjectile>();
        if (!proj) { Destroy(go); return; }

        var cfg = new BeatProjectile.Config
        {
            direction = dir,                         // movement
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

        proj.Initialize(cfg);

        if (debugLogs)
            Debug.Log($"[TurretShooter] Fired beat={_beatCount}, dir={dir}, prefabRot={prefabRotation} ({zDeg}°)");
    }

    // Unity version-safe finder
    static T FindOne<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER || UNITY_2022_3_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
