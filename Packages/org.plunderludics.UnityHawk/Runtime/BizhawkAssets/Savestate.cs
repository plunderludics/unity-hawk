using BizHawk.Emulation.Common;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityHawk {

public class Savestate : BizhawkAsset {
    [FormerlySerializedAs("GameInfo")] [Header("Game")]
    public RomInfo RomInfo;

    // TODO: get the rom associated with this game using gamedb
    // public Rom Rom;

    // public string Name {
    //     get => RomInfo.Name != "NULL" ? RomInfo.Name : name;
    // }
}
}