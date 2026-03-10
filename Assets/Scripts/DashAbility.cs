using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class DashAbility : NetworkBehaviour
{
    [Header("Dash Settings")]
    [SerializeField] private float dashDistance     = 8f;
    [SerializeField] private float dashDuration     = 0.15f;
    [SerializeField] private float cooldown         = 8f;
    [SerializeField] private float castLockDuration = 0.1f;

    [Header("Arrow Visual")]
    [SerializeField] private float arrowShaftWidth  = 0.15f;
    [SerializeField] private float arrowHeadLength  = 1.2f;
    [SerializeField] private float arrowHeadWidth   = 0.6f;

    public float CooldownFraction  => Mathf.Clamp01((_nextDashTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextDashTime - Time.time);
    public bool  IsAiming          => _aiming;

    private static readonly Vector3 MoveForward = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 MoveRight   = new Vector3(1f, 0f, -1f).normalized;

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);

    private PlayerController _pc;
    private PlayerHealth     _health;
    private NavMeshAgent     _agent;
    private LineRenderer     _arrow;

    private bool      _aiming;
    private float     _nextDashTime;
    private Coroutine _dashCoroutine;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        _pc     = GetComponent<PlayerController>();
        _agent  = GetComponent<NavMeshAgent>();
        _health = GetComponent<PlayerHealth>();
        CreateArrowVisual();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_dashCoroutine != null) return;

        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null && _health.IsDead)
        {
            CancelAim();
            return;
        }

        bool pressed  = GameSettings.UseWasd
            ? Keyboard.current.leftShiftKey.wasPressedThisFrame
            : Keyboard.current.wKey.wasPressedThisFrame;
        bool released = GameSettings.UseWasd
            ? Keyboard.current.leftShiftKey.wasReleasedThisFrame
            : Keyboard.current.wKey.wasReleasedThisFrame;

        if (pressed && Time.time >= _nextDashTime)
            StartAim();

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
                Vector3 dir = GetAimDirection();
                CancelAim();
                _nextDashTime  = Time.time + cooldown;
                _dashCoroutine = StartCoroutine(ExecuteDash(dir));
            }
        }
    }

    private void StartAim()
    {
        _aiming = true;
        _arrow.enabled = true;
    }

    public void CancelAim()
    {
        _aiming = false;
        if (_arrow != null) _arrow.enabled = false;
    }

    private Vector3 GetAimDirection()
    {
        if (GameSettings.UseWasd)
        {
            float fwd   = 0f;
            float right = 0f;
            if (Keyboard.current.wKey.isPressed) fwd   += 1f;
            if (Keyboard.current.sKey.isPressed) fwd   -= 1f;
            if (Keyboard.current.dKey.isPressed) right += 1f;
            if (Keyboard.current.aKey.isPressed) right -= 1f;

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
        Vector3 dir = GetAimDirection();
        Vector3 origin = new Vector3(transform.position.x, 0.05f, transform.position.z);

        Vector3 shaftEnd = origin + dir * (dashDistance - arrowHeadLength);
        Vector3 tip      = origin + dir * dashDistance;
        Vector3 perp     = new Vector3(-dir.z, 0f, dir.x) * (arrowHeadWidth * 0.5f);

        _arrow.SetPosition(0, origin);
        _arrow.SetPosition(1, shaftEnd);
        _arrow.SetPosition(2, shaftEnd + perp);
        _arrow.SetPosition(3, tip);
        _arrow.SetPosition(4, shaftEnd - perp);
        _arrow.SetPosition(5, shaftEnd);
    }

    private IEnumerator ExecuteDash(Vector3 dir)
    {
        GetComponent<AutoAttacker>()?.CancelAutoAttack();
        GetComponent<TripleShotAbility>()?.CancelCharge();

        Vector3 startPos    = transform.position;
        Vector3 rawEnd      = startPos + dir * dashDistance;
        Vector3 endPos      = startPos;

        if (NavMesh.SamplePosition(rawEnd, out NavMeshHit hit, dashDistance, NavMesh.AllAreas))
            endPos = hit.position;

        float totalDur = dashDuration + castLockDuration;
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

        // castLockDuration freeze — movement stays locked, player waits
        float lockEnd = Time.time + castLockDuration;
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
