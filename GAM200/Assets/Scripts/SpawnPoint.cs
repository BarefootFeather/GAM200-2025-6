// SpawnPoint.cs
using UnityEngine;

[RequireComponent(typeof(Collider2D))] // or Collider for 3D, keep 2D for your project
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Player must have tag 'Player'")]
    public string playerTag = "Player";

    void Reset()
    {
        var c2d = GetComponent<Collider2D>();
        if (c2d) c2d.isTrigger = true;
        var c3d = GetComponent<Collider>();
        if (c3d) c3d.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag) && RespawnManager.I)
            RespawnManager.I.SetSpawn(transform);
    }

    // Optional gizmo
    void OnDrawGizmos()
    {
        Gizmos.DrawIcon(transform.position, "sv_icon_dot14_pix16_gizmo", true);
    }
}
