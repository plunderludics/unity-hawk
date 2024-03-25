using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

[CreateAssetMenu(fileName = "GenericInputMapping", menuName = "plunderludics/new generic input mapping", order = 0)]
public class GenericInputMappingObject : ScriptableObject {
#if ENABLE_INPUT_SYSTEM
    [FormerlySerializedAs("mappings")]
    [Tooltip("Mappings from unity input action to bizhawk key name")]
    public List<GenericInputProvider.Action2Key> All;
#endif
}

}