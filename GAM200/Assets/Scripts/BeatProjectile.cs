using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BeatProjectile : MonoBehaviour
{
 
    public struct Config
    {
        public Vector2 direction;                    // move direction
        public float stepDistance;                   // distance per beat (e.g., 1 cell)
        public bool snapToGrid;                      // snap using step distance if no tilemap
        public bool smoothStep;                      // glide between cells
        public float moveLerpFractionOfInterval;     // 0.05..0.9 fraction of one interval
        public int bpmForSmoothing;                  // used ONLY to compute glide duration
        public float intervalBeatsForSmoothing;      // usually 1
        public Tilemap gridTilemap;                  // optional, to center on cells
        public int maxSteps;                         // self-destruct after this many steps
        public int damage;                           // damage to PlayerController
        public PlayerController player;              // for grid-cell hit (same as EnemyController)
    }

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    [Header("Collision LOGGING (does NOT block)")]
    [Tooltip("Log colliders at the destination using Physics2D OverlapBox.")]
    [SerializeField] private bool logPhysicsCollisions = true;

    [Tooltip("If true, auto-use BoxCollider2D size (if found).")]
    [SerializeField] private bool useColliderSize = true;

    [Tooltip("Fallback/override size used for OverlapBox if no BoxCollider2D or auto is off.")]
    [SerializeField] private Vector2 boxCheckSize = new Vector2(0.8f, 0.8f);

    [Tooltip("Include trigger colliders in logs.")]
    [SerializeField] private bool includeTriggers = false;

    [Tooltip("Layer mask for physics logging.")]
    [SerializeField] private LayerMask physicsMask = ~0;

    [Tooltip("Log if destination cell on this Tilemap has a tile (e.g., walls).")]
    [SerializeField] private bool logTilemapCollisions = false;

    [Tooltip("Tilemap used for tile-based collision logging (e.g., Walls).")]
    [SerializeField] private Tilemap collisionTilemap;

 
    private Vector2 _dir = Vector2.right;
    private float _step = 1f;
    private bool _snap;
    private bool _smooth;
    private float _lerpFrac = 0.35f;
    private int _bpmForSmoothing = 120;
    private float _intervalBeatsForSmoothing = 1f;
    private Tilemap _tilemap;
    private int _maxSteps = 16;
    private int _damage = 1;
    private PlayerController _player;

    // State
    private int _stepsTaken = 0;
    private int _lastProcessedBeat = int.MinValue;
    private Coroutine _moveCo;

    // Cached components (for logging size)
    private BoxCollider2D _cachedBox;

 
    public void Initialize(Config c)
    {
        _dir = c.direction.sqrMagnitude > 0 ? c.direction.normalized : Vector2.right;
        _step = Mathf.Max(0.01f, c.stepDistance);
        _snap = c.snapToGrid;
        _smooth = c.smoothStep;
        _lerpFrac = Mathf.Clamp(c.moveLerpFractionOfInterval, 0.05f, 0.9f);
        _bpmForSmoothing = Mathf.Max(1, c.bpmForSmoothing);
        _intervalBeatsForSmoothing = Mathf.Max(0.0001f, c.intervalBeatsForSmoothing);
        _tilemap = c.gridTilemap;
        _maxSteps = Mathf.Max(1, c.maxSteps);
        _damage = Mathf.Max(0, c.damage);
        _player = c.player;
    }


    private void Awake()
    {
        _cachedBox = GetComponent<BoxCollider2D>();
    }

    private void OnEnable() { BeatBus.Beat += OnBeat; }
    private void OnDisable() { BeatBus.Beat -= OnBeat; }


    private void OnBeat(int beatIndex)
    {
        // guard in case multiple events fire in same frame
        if (beatIndex == _lastProcessedBeat) return;
        _lastProcessedBeat = beatIndex;

        StepOnce();
    }

    private void StepOnce()
    {
        if (_stepsTaken >= _maxSteps)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 from = transform.position;
        Vector2 to = from + _dir * _step;

        // align to tile center if tilemap provided; else optional snap by stepDistance
        if (_tilemap)
        {
            Vector3Int cell = _tilemap.WorldToCell(to);
            to = _tilemap.GetCellCenterWorld(cell);
        }
        else if (_snap)
        {
            to = Snap(to, _step);
        }

 
        LogPotentialCollisions(from, to);


        if (_tilemap && _player != null)
        {
            Vector3Int projCell = _tilemap.WorldToCell(to);
            Vector3Int playerCell = _tilemap.WorldToCell(_player.transform.position);

            if (projCell == playerCell && !_player.IsInvulnerable())
            {
                _player.TakeDamage(_damage);

                if (debugLogs)
                    Debug.Log($"[BeatProjectile] Player and projectile both at grid {projCell}, dealt {_damage} damage");

                Destroy(gameObject); // remove projectile on hit
                return;
            }
        }

        // move
        MoveTo(to);
        _stepsTaken++;
    }


    private void LogPotentialCollisions(Vector2 from, Vector2 to)
    {
        // Physics: OverlapBox at the DESTINATION position (logging only)
        if (logPhysicsCollisions)
        {
            Vector2 size = useColliderSize && _cachedBox != null ? _cachedBox.size : boxCheckSize;
            float angle = transform.eulerAngles.z;

            var hits = Physics2D.OverlapBoxAll(to, size, angle, physicsMask);
            foreach (var h in hits)
            {
                if (!h) continue;
                if (!includeTriggers && h.isTrigger) continue;
                if (h.transform == transform) continue; // skip self

                Debug.Log($"[BeatProjectile] Would collide with '{h.name}' at {to}");
            }
        }

        // Tilemap: log if the destination cell has a tile
        if (logTilemapCollisions && collisionTilemap != null)
        {
            Vector3Int cell = collisionTilemap.WorldToCell(to);
            if (collisionTilemap.HasTile(cell))
                Debug.Log($"[BeatProjectile] Would collide with Tilemap at cell {cell} (world {to})");
        }
    }

    private void MoveTo(Vector2 nextPos)
    {
        if (!_smooth)
        {
            transform.position = nextPos;
            return;
        }

        // we don't read BPMController; use smoothing params to compute glide duration
        float intervalSec = (60f / _bpmForSmoothing) * _intervalBeatsForSmoothing;
        float dur = Mathf.Max(0.01f, intervalSec * _lerpFrac);

        if (_moveCo != null) StopCoroutine(_moveCo);
        _moveCo = StartCoroutine(LerpTo(nextPos, dur));
    }

    private IEnumerator LerpTo(Vector2 target, float dur)
    {
        Vector2 start = transform.position;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            transform.position = Vector2.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    private static Vector2 Snap(Vector2 p, float cell)
    {
        if (cell <= 0f) return p;
        float x = Mathf.Round(p.x / cell) * cell;
        float y = Mathf.Round(p.y / cell) * cell;
        return new Vector2(x, y);
    }
}
