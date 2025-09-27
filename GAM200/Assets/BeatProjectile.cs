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
        public PlayerController player; // NEW: pass player reference
    }

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

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

    private int _stepsTaken = 0;
    private int _lastProcessedBeat = int.MinValue;
    private Coroutine _moveCo;

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

    private void OnEnable() { BeatBus.Beat += OnBeat; }
    private void OnDisable() { BeatBus.Beat -= OnBeat; }

    private void OnBeat(int beatIndex)
    {
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

        if (_tilemap)
        {
            Vector3Int cell = _tilemap.WorldToCell(to);
            to = _tilemap.GetCellCenterWorld(cell);
        }
        else if (_snap)
        {
            to = Snap(to, _step);
        }

        // ✅ Grid-cell collision (same as EnemyController)
        if (_tilemap && _player != null)
        {
            Vector3Int projCell = _tilemap.WorldToCell(to);
            Vector3Int playerCell = _tilemap.WorldToCell(_player.transform.position);

            if (projCell == playerCell && !_player.IsInvulnerable())
            {
                _player.TakeDamage(_damage);

                if (debugLogs)
                    Debug.Log($"[BeatProjectile] Hit Player at cell {projCell}, dealt {_damage} dmg");

                Destroy(gameObject); // remove projectile on hit
                return;
            }
        }

        MoveTo(to);
        _stepsTaken++;
    }

    private void MoveTo(Vector2 nextPos)
    {
        if (!_smooth)
        {
            transform.position = nextPos;
            return;
        }

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
