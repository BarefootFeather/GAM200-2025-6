using UnityEngine;

public class EnemyController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void CallOnInterval()
    {
        foreach (EnemyScript script in GetComponentsInChildren<EnemyScript>())
        {
            if (script != null)
                script.OnIntervalReached();
        }
    }
}
