using UnityEngine;
using UnityEngine.Tilemaps;

public class TurretShooter : MonoBehaviour
{
    public enum AimMode { Cardinal, Vector2 }

    [Header("Beat Scheduling")]
    [SerializeField] int fireEveryN = 6;
    [SerializeField] int fireOffset = 0;

    [Header("Projectile")]
    [SerializeField] GameObject projectilePrefab;     // BeatProjectile on root
    [SerializeField] Transform muzzle;                // optional
    [SerializeField] int projectileDamage = 1;
    [SerializeField] int projectileMaxSteps = 12;
    [SerializeField] float projectileStepDistance = 1f;
    [SerializeField] bool projectileSnapToGrid = true;
    [SerializeField] bool projectileSmoothStep = true;
    [SerializeField, Range(0.05f, 0.9f)] float projectileLerpFraction = 0.35f;

    [Header("Smoothing (no BPM access)")]
    [SerializeField] int bpmForSmoothing = 120;
    [SerializeField] float intervalBeatsForSmoothing = 1f;

    [Header("Direction")]
    [SerializeField] AimMode aimMode = AimMode.Cardinal;
    [SerializeField] Vector2 cardinalDir = Vector2.right;  // (1,0),(0,1),(-1,0),(0,-1)
    [SerializeField] Vector2 shootDir = Vector2.right;     // used if Vector2 mode

    [Header("Collisions")]
    [SerializeField] PlayerController player;               // PlayerController, not Transform
    [SerializeField] Tilemap gridTilemap;                   // same as Player’s

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
        if (IsScheduled(_beatCount, fireEveryN, fireOffset)) FireOnce();
    }

    static bool IsScheduled(int count, int everyN, int offset) =>
        ((count - offset) % Mathf.Max(1, everyN)) == 0;

    void FireOnce()
    {
        if (!projectilePrefab || !player || !gridTilemap)
        {
            if (debugLogs)
                Debug.LogWarning($"[TurretShooter] Missing refs. prefab:{projectilePrefab}, player:{player}, tilemap:{gridTilemap}");
            return;
        }

        Vector3 spawnPos = muzzle ? muzzle.position : transform.position;
        var go = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        var proj = go.GetComponent<BeatProjectile>();
        if (!proj) { Destroy(go); return; }

        Vector2 dir = aimMode == AimMode.Cardinal ? CardinalToUnit(cardinalDir) : shootDir.normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

        var cfg = new BeatProjectile.Config
        {
            direction = dir,
            stepDistance = projectileStepDistance,
            snapToGrid = projectileSnapToGrid,
            smoothStep = projectileSmoothStep,
            moveLerpFractionOfInterval = projectileLerpFraction,
            bpmForSmoothing = bpmForSmoothing,
            intervalBeatsForSmoothing = intervalBeatsForSmoothing,
            gridTilemap = gridTilemap,
            maxSteps = projectileMaxSteps,
            damage = projectileDamage,
            player = player
        };

        proj.Initialize(cfg);

        if (debugLogs)
            Debug.Log($"[TurretShooter] Fired beat={_beatCount} dir={dir}");
    }

    static Vector2 CardinalToUnit(Vector2 raw)
    {
        // snap to nearest axis
        return Mathf.Abs(raw.x) >= Mathf.Abs(raw.y)
            ? new Vector2(raw.x == 0 ? 1f : Mathf.Sign(raw.x), 0f)
            : new Vector2(0f, raw.y == 0 ? 1f : Mathf.Sign(raw.y));
    }

    // non-obsolete single finder with fallback
    static T FindOne<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER || UNITY_2022_3_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
