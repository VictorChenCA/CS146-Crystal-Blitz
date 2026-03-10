using UnityEngine;

/// <summary>
/// Controls a set of barrier wall colliders/renderers around a team spawn.
/// Children start with colliders + renderers disabled.
/// GamePhaseManager enables them at InGame start and disables after 10s.
/// </summary>
public class SpawnBarrierController : MonoBehaviour
{
    [SerializeField] public int TeamIndex = 0;

    private Collider[]  _walls;
    private Renderer[]  _renderers;

    private void Awake()
    {
        _walls     = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
        SetActive(false);
    }

    public void SetActive(bool active)
    {
        foreach (var c in _walls)     c.enabled = active;
        foreach (var r in _renderers) r.enabled = active;
    }
}
