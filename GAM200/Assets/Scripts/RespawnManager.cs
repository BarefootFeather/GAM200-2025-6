// RespawnManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class RespawnManager : MonoBehaviour
{
    public static RespawnManager I;

    private Vector3? _spawnPos;
    private Quaternion _spawnRot = Quaternion.identity;
    private string _sceneName;

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetSpawn(Transform t)
    {
        _spawnPos = t.position;
        _spawnRot = t.rotation;
        _sceneName = SceneManager.GetActiveScene().name;
    }

    public Vector3 GetSpawnOr(Vector3 fallback)
    {
        if (_spawnPos.HasValue && SceneManager.GetActiveScene().name == _sceneName)
            return _spawnPos.Value;
        return fallback;
    }

    public Quaternion GetSpawnRotOr(Quaternion fallback)
    {
        if (_spawnPos.HasValue && SceneManager.GetActiveScene().name == _sceneName)
            return _spawnRot;
        return fallback;
    }

    public void Respawn()
    {
        // Reload the active scene → everything resets to original state.
        var active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.name);
    }
}
