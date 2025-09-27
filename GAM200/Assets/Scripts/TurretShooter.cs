using UnityEngine;
using UnityEngine.Tilemaps;

public class TurretShooter : MonoBehaviour
{
    // Keep the enum plain (no attributes here)
    public enum AimMode { Cardinal, ForwardTransform, AtPlayer }

    [Header("Beat Scheduling")]
    [SerializeField] private int fireEveryN = 1;
    [SerializeField] private int fireOffset = 0;

    [Header("Projectile")]
    [SerializeField] private BeatProjectile projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private int projectileDamage = 1;
    [SerializeField] private int projectileMaxSteps = 12;
    [SerializeField] private float projectileStepDistance = 1f;
    [SerializeField] private bool projectileSnapToGrid = true;
    [SerializeField] private bool projectileSmoothStep = true;
    [Range(0.05f, 0.9f)]
    [SerializeField] private float projectileLerpFraction = 0.35f;

    [Header("Smoothing (no BPM access)")]
    [SerializeField] private int bpmForSmoothing = 120;
    [SerializeField] private float intervalBeatsForSmoothing = 1f;

    [Header("Direction")]
    [SerializeField] private AimMode aimMode = AimMode.Cardinal;
    [SerializeField] private Vector2 cardinalDir = Vector2.right;
    [SerializeField] private Transform player;

    [Header("Tilemap (optional)")]
    [SerializeField] private Tilemap gridTilemap;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private int _localCounter = -1;

    private void OnEnable() { BeatBus.Beat += OnBeat; }
    private void OnDisable() { BeatBus.Beat -= OnBeat; }

    private void OnBeat(int idx)
    {
        _localCounter++;
        if (((_localCounter - fireOffset) % Mathf.Max(1, fireEveryN)) != 0) return;
        Fire();
    }

    private void Fire()
    {
        if (!projectilePrefab) return;

        Transform origin = muzzle ? muzzle : transform;
        Vector3 spawnPos = origin.position;
        Vector2 dir = ComputeDirection(origin);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

        var proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        var pc = player ? player.GetComponent<PlayerController>() : null;

        proj.Initialize(new BeatProjectile.Config
        {
            direction = dir.normalized,
            stepDistance = projectileStepDistance,
            snapToGrid = projectileSnapToGrid,
            smoothStep = projectileSmoothStep,
            moveLerpFractionOfInterval = projectileLerpFraction,
            bpmForSmoothing = bpmForSmoothing,
            intervalBeatsForSmoothing = intervalBeatsForSmoothing,
            gridTilemap = gridTilemap,
            maxSteps = projectileMaxSteps,
            damage = projectileDamage,
            player = pc   // <-- required for grid-cell damage
        });


        if (debugLogs) Debug.Log($"[TurretShooter] Fired projectile dir={dir} at {spawnPos}");
    }

    private Vector2 ComputeDirection(Transform origin)
    {
        switch (aimMode)
        {
            case AimMode.Cardinal: return (cardinalDir.sqrMagnitude > 0) ? cardinalDir.normalized : Vector2.right;
            case AimMode.ForwardTransform: return origin.right; // 2D "forward"
            case AimMode.AtPlayer: return player ? (player.position - origin.position).normalized : Vector2.right;
            default: return Vector2.right;
        }
    }
}
