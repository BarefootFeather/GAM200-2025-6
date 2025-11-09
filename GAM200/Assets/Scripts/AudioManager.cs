using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("---------Audio Source---------")]
    [SerializeField] AudioSource SFXSource;

    [Header("---------Audio Clip---------")]
    public AudioClip dash;
    public AudioClip attack;
    public AudioClip damage;
    public AudioClip playerdeath;
    public AudioClip enemydeath;
    public AudioClip collectible;
    public AudioClip shield;
    public AudioClip trap;
    public AudioClip wall_move;
    public AudioClip bullet;
    public AudioClip button;
}
