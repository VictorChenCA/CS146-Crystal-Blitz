using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class HatManager : NetworkBehaviour
{
    [SerializeField] private GameObject[] _hatPrefabs;   // 11 slots: 0–10

    private NetworkVariable<int> _hatIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Transform   _hatPoint;
    private GameObject  _activeHat;

    public override void OnNetworkSpawn()
    {
        _hatIndex.OnValueChanged += OnHatIndexChanged;

        _hatPoint = transform.Find("HatPoint");

        // Apply whatever hat is already set (e.g. late-joining client)
        ApplyHat(_hatIndex.Value);

        if (!IsOwner) return;

        int saved = PlayerPrefs.GetInt("hatIndex", -1);
        if (saved != _hatIndex.Value)
            RequestHatServerRpc(saved);
    }

    public override void OnNetworkDespawn()
    {
        _hatIndex.OnValueChanged -= OnHatIndexChanged;

        if (IsOwner)
            PlayerPrefs.SetInt("hatIndex", _hatIndex.Value);
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            int next = _hatIndex.Value - 1;
            if (next < -1) next = _hatPrefabs.Length - 1;
            RequestHatServerRpc(next);
        }
        else if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            int next = _hatIndex.Value + 1;
            if (next >= _hatPrefabs.Length) next = -1;
            RequestHatServerRpc(next);
        }
    }

    // ── Public API for lobby hat selector UI ──────────────────────────────────
    public int    HatCount              => _hatPrefabs != null ? _hatPrefabs.Length : 0;
    public int    CurrentHat            => _hatIndex.Value;
    public string GetHatName(int index) => index < 0 ? "None"
                                         : (index < _hatPrefabs.Length && _hatPrefabs[index] != null)
                                           ? _hatPrefabs[index].name
                                           : $"Hat {index + 1}";
    public void   SelectHat(int index)  => RequestHatServerRpc(index);

    [Rpc(SendTo.Server)]
    private void RequestHatServerRpc(int index)
    {
        if (index < -1 || index >= _hatPrefabs.Length) return;
        _hatIndex.Value = index;
    }

    private void OnHatIndexChanged(int previous, int current) => ApplyHat(current);

    private void ApplyHat(int index)
    {
        if (_activeHat != null)
        {
            Destroy(_activeHat);
            _activeHat = null;
        }

        if (index < 0 || index >= _hatPrefabs.Length || _hatPrefabs[index] == null) return;
        if (_hatPoint == null) return;

        _activeHat = Instantiate(_hatPrefabs[index], _hatPoint);
        _activeHat.transform.localPosition = Vector3.zero;
        _activeHat.transform.localRotation = Quaternion.identity;
    }
}
