using UnityEngine;

public class PooledObject : MonoBehaviour
{
    private NotePool pool;

    public void SetPool(NotePool pool)
    {
        this.pool = pool;
    }

    public void Release()
    {
        if (pool != null)
            pool.Despawn(gameObject);
        else
            Destroy(gameObject);
    }
}
