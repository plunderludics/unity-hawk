#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "GenericInputMapping", menuName = "plunderludics/new generic input mapping", order = 0)]
public class GenericInputMapping : ScriptableObject {
  [System.Serializable]
    public struct Action2Key {
        public InputActionReference action;
        public string keyName;
    }

    [UnityEngine.Serialization.FormerlySerializedAs("mappings")]
    [Tooltip("the list of Action to Key for bizhawk")]
    public List<Action2Key> All;
}
#endif