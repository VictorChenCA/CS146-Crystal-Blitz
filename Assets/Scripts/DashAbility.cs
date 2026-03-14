using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class DashAbility : NetworkBehaviour
{
    [Header("Dash Settings")]
    [SerializeField] private float dashDistance      = 8f;
    [SerializeField] private float dashDuration      = 0.15f;
    [SerializeField] private float cooldown          = 8f;

    [Header("Cast Timing")]
    [SerializeField] private float castDuration      = 0.1f;
    [SerializeField] private float animationDuration = 0.1f;  // post-dash freeze (was castLockDuration)

    [Header("Arrow Visual")]
    [SerializeField] private float arrowShaftWidth  = 0.15f;
    [SerializeField] private float arrowHeadLength  = 1.2f;
    [SerializeField] private float arrowHeadWidth   = 0.6f;

    public float CooldownFraction  => Mathf.Clamp01((_nextDashTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextDashTime - Time.time);
    public bool  IsAiming          => _aiming;
    public float CastFraction      => _castFraction;
    public float ManaCost          => manaCost;

    private static readonly Vector3 MoveForward = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 MoveRight   = new Vector3(1f, 0f, -1f).normalized;

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);

    [SerializeField] private float manaCost = 10f;

    private PlayerController _pc;
    private PlayerHealth     _health;
    private PlayerMana       _mana;
    private PlayerXP         _xp;
    private NavMeshAgent     _agent;
    private LineRenderer     _arrow;

    private bool      _aiming;
    private float     _nextDashTime;
    private float     _castFraction;
    private Coroutine _dashCoroutine;
    private Coroutine _castCoroutine;

    public override void OnNetworkSpawn()
    {
        _pc    = GetComponent<PlayerController>();
        _agent = GetComponent<NavMeshAgent>();
        CreateArrowVisual();

        if (!IsOwner) { enabled = false; return; }
        _health = GetComponent<PlayerHealth>();
        _mana   = GetComponent<PlayerMana>();
        _xp     = GetComponent<PlayerXP>();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_dashCoroutine != null || _castCoroutine != null) return;

        if (_pc != null && _pc.CharacterIndex.Value != 1) { CancelAim(); return; }

        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null && _health.IsDead)
        {
            CancelAim();
            return;
        }
        if (GameManager.ChatOpen) return;

        bool pressed  = GameSettings.UseWasd
            ? GameKeybinds.WasPressedThisFrame(GameKeybinds.Wasd_Ability2)
            : GameKeybinds.WasPressedThisFrame(GameKeybinds.PnC_Ability2);
        bool released = GameSettings.UseWasd
            ? GameKeybinds.WasReleasedThisFrame(GameKeybinds.Wasd_Ability2)
            : GameKeybinds.WasReleasedThisFrame(GameKeybinds.PnC_Ability2);

        if (pressed && Time.time >= _nextDashTime)
        {
            if (_mana != null && !_mana.HasMana(manaCost)) return;
            StartAim();
        }

        if (_aiming)
        {
            UpdateArrow();

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelAim();
                return;
            }

            if (released)
            {
                Vector3 dir           = GetAimDirection();
                float   effectiveDist = GetEffectiveDashDistance(dir);
                CancelAim();
                _castCoroutine = StartCoroutine(CastCoroutine(dir, effectiveDist));
            }
        }
    }

    private IEnumerator CastCoroutine(Vector3 dir, float effectiveDist)
    {
        // Cast phase: fill _castFraction 0→1
        float castEnd = Time.time + castDuration;
        while (Time.time < castEnd)
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                _castFraction  = 0f;
                _castCoroutine = null;
                yield break;
            }
            _castFraction = castDuration > 0f ? 1f - (castEnd - Time.time) / castDuration : 1f;
            yield return null;
        }
        _castFraction = 0f;

        // Commit
        _nextDashTime  = Time.time + cooldown;
        _mana?.SpendManaServerRpc(manaCost);
        TutorialManager.OnWFired?.Invoke();
        _castCoroutine = null;
        _dashCoroutine = StartCoroutine(ExecuteDash(dir, effectiveDist));
    }

    private float GetEffectiveDashDistance(Vector3 dir)
    {
        float xpLevel     = _xp?.Level.Value ?? 1;
        float scaledDist  = dashDistance * (1f + 0.05f * (xpLevel - 1));

        // In P&C mode clamp to cursor distance so the player dashes to the cursor if closer
        if (!GameSettings.UseWasd && Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
            if (_groundPlane.Raycast(ray, out float dist))
            {
                Vector3 hit        = ray.GetPoint(dist);
                float   cursorDist = Vector3.Distance(
                    new Vector3(transform.position.x, 0f, transform.position.z),
                    new Vector3(hit.x, 0f, hit.z));
                scaledDist = Mathf.Min(cursorDist, scaledDist);
            }
        }
        return scaledDist;
    }

    private void StartAim()
    {
        _aiming = true;
        _arrow.enabled = true;
        Vector3 dir = GetAimDirection();
        BroadcastAimStartRpc(new Vector3(transform.position.x, 0.05f, transform.position.z), dir);
    }

    public void CancelAim()
    {
        _aiming = false;
        if (_castCoroutine != null)
        {
            StopCoroutine(_castCoroutine);
            _castCoroutine = null;
            _castFraction  = 0f;
        }
        if (_arrow != null) _arrow.enabled = false;
        BroadcastAimEndRpc();
    }

    [Rpc(SendTo.NotOwner)]
    private void BroadcastAimStartRpc(Vector3 origin, Vector3 dir)
    {
        if (_arrow == null) return;
        _arrow.enabled = true;
        float dist     = dashDistance;
        Vector3 shaftEnd = origin + dir * (dist - arrowHeadLength);
        Vector3 tip      = origin + dir * dist;
        Vector3 perp     = new Vector3(-dir.z, 0f, dir.x) * (arrowHeadWidth * 0.5f);
        _arrow.SetPosition(0, origin);
        _arrow.SetPosition(1, shaftEnd);
        _arrow.SetPosition(2, shaftEnd + perp);
        _arrow.SetPosition(3, tip);
        _arrow.SetPosition(4, shaftEnd - perp);
        _arrow.SetPosition(5, shaftEnd);
    }

    [Rpc(SendTo.NotOwner)]
    private void BroadcastAimEndRpc()
    {
        if (_arrow != null) _arrow.enabled = false;
    }

    private Vector3 GetAimDirection()
    {
        if (GameSettings.UseWasd)
        {
            float fwd   = 0f;
            float right = 0f;
            if (GameKeybinds.IsPressed(GameKeybinds.Wasd_MoveForward)) fwd   += 1f;
            if (GameKeybinds.IsPressed(GameKeybinds.Wasd_MoveBack))    fwd   -= 1f;
            if (GameKeybinds.IsPressed(GameKeybinds.Wasd_MoveRight))   right += 1f;
            if (GameKeybinds.IsPressed(GameKeybinds.Wasd_MoveLeft))    right -= 1f;

            if (fwd == 0f && right == 0f)
                return _pc != null ? _pc.LastMoveDirection : MoveForward;

            return (MoveForward * fwd + MoveRight * right).normalized;
        }
        else
        {
            if (Mouse.current == null) return MoveForward;
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
            if (_groundPlane.Raycast(ray, out float dist))
            {
                Vector3 hit = ray.GetPoint(dist);
                Vector3 delta = new Vector3(hit.x - transform.position.x, 0f, hit.z - transform.position.z);
                if (delta.sqrMagnitude > 0.001f)
                    return delta.normalized;
            }
            return MoveForward;
        }
    }

    private void UpdateArrow()
    {
        float   xpLevel  = _xp?.Level.Value ?? 1;
        float   dist     = dashDistance * (1f + 0.05f * (xpLevel - 1));
        Vector3 dir      = GetAimDirection();
        Vector3 origin   = new Vector3(transform.position.x, 0.05f, transform.position.z);

        Vector3 shaftEnd = origin + dir * (dist - arrowHeadLength);
        Vector3 tip      = origin + dir * dist;
        Vector3 perp     = new Vector3(-dir.z, 0f, dir.x) * (arrowHeadWidth * 0.5f);

        _arrow.SetPosition(0, origin);
        _arrow.SetPosition(1, shaftEnd);
        _arrow.SetPosition(2, shaftEnd + perp);
        _arrow.SetPosition(3, tip);
        _arrow.SetPosition(4, shaftEnd - perp);
        _arrow.SetPosition(5, shaftEnd);
    }

    private IEnumerator ExecuteDash(Vector3 dir, float effectiveDist)
    {
        GetComponent<AutoAttacker>()?.CancelAutoAttack();
        GetComponent<TripleShotAbility>()?.CancelCharge();
        GetComponent<FanShotAbility>()?.CancelCharge();

        Vector3 startPos    = transform.position;
        Vector3 rawEnd      = startPos + dir * effectiveDist;
        Vector3 endPos      = startPos;

        if (NavMesh.SamplePosition(rawEnd, out NavMeshHit hit, effectiveDist, NavMesh.AllAreas))
            endPos = new Vector3(hit.position.x, startPos.y, hit.position.z);

        float totalDur = dashDuration + animationDuration;
        _pc?.LockMovement(totalDur);
        _pc?.StopNavMovement();

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            if (_health != null && _health.IsDead)
            {
                _pc?.CancelMovementLock();
                _dashCoroutine = null;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dashDuration));
            Vector3 newPos = Vector3.Lerp(startPos, endPos, t);

            transform.position = newPos;
            if (_agent != null && _agent.enabled)
                _agent.nextPosition = newPos;
            _pc?.ForceSubmitPosition(newPos);

            yield return null;
        }

        transform.position = endPos;
        if (_agent != null && _agent.enabled)
            _agent.Warp(endPos);
        _pc?.ForceSubmitPosition(endPos);

        // animationDuration freeze — movement stays locked, player waits
        float lockEnd = Time.time + animationDuration;
        while (Time.time < lockEnd)
        {
            if (_health != null && _health.IsDead)
            {
                _pc?.CancelMovementLock();
                _dashCoroutine = null;
                yield break;
            }
            yield return null;
        }

        _dashCoroutine = null;
    }

    private void CreateArrowVisual()
    {
        var go = new GameObject("DashArrow");
        go.transform.SetParent(transform);

        _arrow = go.AddComponent<LineRenderer>();
        _arrow.positionCount     = 6;
        _arrow.loop              = false;
        _arrow.useWorldSpace     = true;
        _arrow.startWidth        = arrowShaftWidth;
        _arrow.endWidth          = arrowShaftWidth;
        _arrow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _arrow.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.4f, 1f, 0.55f, 0.75f);
        _arrow.material = mat;

        _arrow.enabled = false;
    }
}
