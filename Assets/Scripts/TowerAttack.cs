using UnityEngine;
using System.Collections;
using Unity.Netcode;

/// <summary>
/// Server-authoritative tower attack. Finds nearest enemy player in range
/// and applies direct damage. Uses a simple LineRenderer flash for VFX.
/// </summary>
public class TowerAttack : NetworkBehaviour
{
    [SerializeField] private float range     = 15f;
    [SerializeField] private float damage    = 80f;
    [SerializeField] private float cooldown  = 2f;
    [SerializeField] public  int   teamIndex = 0;

    private float           _nextAttackTime;
    private StructureHealth _myStructure;
    private LineRenderer    _lineRenderer;

    private void Awake()
    {
        _myStructure = GetComponent<StructureHealth>();

        // Line renderer for attack VFX (client and server — synced via RPC)
        _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.positionCount     = 2;
        _lineRenderer.startWidth        = 0.1f;
        _lineRenderer.endWidth          = 0.05f;
        _lineRenderer.useWorldSpace     = true;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lineRenderer.receiveShadows    = false;
        _lineRenderer.enabled           = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = teamIndex == 0
            ? new Color(0.5f, 0.7f, 1f, 1f)
            : new Color(1f, 0.4f, 0.4f, 1f);
        _lineRenderer.material = mat;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (GamePhaseManager.Instance?.Phase.Value != GamePhaseManager.GamePhase.InGame) return;
        if (_myStructure != null && !_myStructure.IsAlive.Value) return;
        if (Time.time < _nextAttackTime) return;

        var target = FindNearestEnemy();
        if (target == null) return;

        _nextAttackTime = Time.time + cooldown;
        target.TakeDamage(damage, ulong.MaxValue);
        TowerAttackVfxRpc(transform.position + Vector3.up, target.transform.position + Vector3.up);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TowerAttackVfxRpc(Vector3 from, Vector3 to)
    {
        StartCoroutine(FlashLine(from, to));
    }

    private IEnumerator FlashLine(Vector3 from, Vector3 to)
    {
        _lineRenderer.SetPosition(0, from);
        _lineRenderer.SetPosition(1, to);
        _lineRenderer.enabled = true;
        yield return new WaitForSeconds(0.2f);
        _lineRenderer.enabled = false;
    }

    private PlayerHealth FindNearestEnemy()
    {
        PlayerHealth nearest    = null;
        float        nearestDist = range + 1f;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            var pc = playerObj.GetComponent<PlayerController>();
            if (pc == null || pc.TeamIndex.Value == teamIndex) continue;

            var ph = playerObj.GetComponent<PlayerHealth>();
            if (ph == null) continue;

            float dist = Vector3.Distance(transform.position, playerObj.transform.position);
            if (dist <= range && dist < nearestDist)
            {
                nearestDist = dist;
                nearest     = ph;
            }
        }
        return nearest;
    }
}
