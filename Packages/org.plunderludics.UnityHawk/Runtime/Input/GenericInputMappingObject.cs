using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

[CreateAssetMenu(fileName = "GenericInputMapping", menuName = "Plunderludics/UnityHawk/GenericInputMapping", order = 0)]
public class GenericInputMappingObject : ScriptableObject {
#if ENABLE_INPUT_SYSTEM
    [FormerlySerializedAs("mappings")]
    [Tooltip("Mappings from unity input action to bizhawk key events")]
    public List<GenericInputProvider.Action2Key> keyMappings;

    [Tooltip("Mappings from unity input action to bizhawk analog inputs")]
    public List<GenericInputProvider.Action2Axis> axisMappings;
#endif
}

}