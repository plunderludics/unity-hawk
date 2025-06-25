using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Plunderludics.UnityHawk.Shared;

using NaughtyAttributes;

namespace UnityHawk {
[ExecuteInEditMode]
public class MemoryApiTool : MonoBehaviour
{
    public Emulator emulator;

    public string hexAddress = "";
    [SerializeField, ReadOnly] string _value;
    public WatchType type = WatchType.Unsigned;
    public int size = 4;
    public bool isBigEndian = false;

    public float valueToWrite = 0f;

    private long _address;

    private int? _currentlyWatchingId;

    void OnEnable() {
        if (emulator == null) {
            emulator = GetComponent<Emulator>();
        }

        OnValidate();
    }

    void OnValidate() {
        if (!long.TryParse(hexAddress, System.Globalization.NumberStyles.HexNumber, null, out _address)) {
            Debug.LogWarning($"Invalid hex address: {hexAddress}");
        }
    }

    [Button]
    void Watch() {
        if (_currentlyWatchingId.HasValue) {
            emulator.Unwatch(_currentlyWatchingId.Value);
        }

        switch (type)
        {
            case WatchType.Unsigned:
                _currentlyWatchingId = emulator.WatchUnsigned(_address, size, isBigEndian, domain: null, value =>
                {
                    _value = value.ToString();
                });
                break;
            case WatchType.Signed:
                _currentlyWatchingId = emulator.WatchSigned(_address, size, isBigEndian, domain: null, value =>
                {
                    _value = value.ToString();
                });
                break;
            case WatchType.Float:
                _currentlyWatchingId = emulator.WatchFloat(_address, isBigEndian, domain: null, value =>
                {
                    _value = value.ToString();
                });
                break;
            default:
                Debug.LogWarning($"Unsupported watch type: {type}");
                break;
        }
    }


    [Button]
    void Write() {
        switch (type) {
            case WatchType.Unsigned:
                emulator.WriteUnsigned(_address, (uint)valueToWrite, size, isBigEndian);
                break;
            case WatchType.Signed:
                emulator.WriteSigned(_address, (int)valueToWrite, size, isBigEndian);
                break;
            case WatchType.Float:
                emulator.WriteFloat(_address, valueToWrite, isBigEndian);
                break;
            default:
                Debug.LogWarning($"Unsupported watch type: {type}");
                break;
        }
    }

    [Button]
    void Freeze() {
        emulator.Freeze(_address, size);
    }

    [Button]
    void Unfreeze() {
        emulator.Unfreeze(_address, size);
    }

}
}