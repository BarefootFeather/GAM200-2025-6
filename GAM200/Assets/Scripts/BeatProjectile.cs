using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BeatProjectile : MonoBehaviour
{
    public struct Config
    {
        public Vector2 direction;
        public float stepDistance;
        public bool snapToGrid;
        public bool smoothStep;
        public float moveLerpFractionOfInterval;
        public int bpmForSmoothing;
        public float intervalBeatsForSmoothing;
        public Tilemap gridTilemap;
        public int maxSteps;
        public int damage;
        public PlayerController player;
    }

    [SerializeField] private bool debugLogs = false;

    // movement / smoothing
    Vector2 _dir = Vector2.right;
    float _step = 1f, _lerpFrac = 0.35f, _intervalBeatsForSmoothing = 1f;
    bool _snap, _smooth;
    int _bpmForSmoothing = 120;

    // refs & damage
    Tilemap _tilemap;
    PlayerController _player;
    int _damageToPlayer = 1;

    // state
    int _maxSteps = 16, _stepsTaken = 0, _lastProcessedBeat = int.MinValue;
    Coroutine _moveCo;

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
        _damageToPlayer = Mathf.Max(0, c.damage);
        _player = c.player;
    }

    void OnEnable() => BeatBus.Beat += OnBeat;
    void OnDisable() => BeatBus.Beat -= OnBeat;

    void OnBeat(int beatIndex)
    {
        if (beatIndex == _lastProcessedBeat) return;
        _lastProcessedBeat = beatIndex;
        StepOnce();
    }

    void StepOnce()
    {
        if (_stepsTaken >= _maxSteps) { Destroy(gameObject); return; }
        if (!_tilemap || !_player) return;

        Vector2 from = transform.position;
        Vector2 to = from + _dir * _step;

        // snap destination to grid center (or step snap fallback)
        Vector3Int destCell = _tilemap.WorldToCell(to);
        Vector2 snappedTo = _tilemap.GetCellCenterWorld(destCell);
        if (_snap && !_tilemap) snappedTo = Snap(to, _step); // (kept for parity)

        // same-grid-cell collision (checks current OR destination cell)
        Vector3Int currCell = _tilemap.WorldToCell(transform.position);
        Vector3Int playerCell = _tilemap.WorldToCell(_player.transform.position);

        if ((currCell == playerCell || destCell == playerCell) && !_player.IsInvulnerable())
        {
            _player.TakeDamage(_damageToPlayer);
            if (debugLogs) Debug.Log($"[BeatProjectile] Player and projectile both at grid {playerCell}, dealt {_damageToPlayer} damage");
            Destroy(gameObject);
            return;
        }

        MoveTo(snappedTo);
        _stepsTaken++;
    }

    void MoveTo(Vector2 nextPos)
    {
        if (!_smooth) { transform.position = nextPos; return; }
        float intervalSec = (60f / _bpmForSmoothing) * _intervalBeatsForSmoothing;
        float dur = Mathf.Max(0.01f, intervalSec * _lerpFrac);
        if (_moveCo != null) StopCoroutine(_moveCo);
        _moveCo = StartCoroutine(LerpTo(nextPos, dur));
    }

    IEnumerator LerpTo(Vector2 target, float dur)
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

    static Vector2 Snap(Vector2 p, float cell)
    {
        if (cell <= 0f) return p;
        return new Vector2(Mathf.Round(p.x / cell) * cell, Mathf.Round(p.y / cell) * cell);
    }
}
