using BizHawk.Emulation.Common;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityHawk {
    public class Savestate : BizhawkAsset
    {
        [FormerlySerializedAs("GameInfo")]
        [Header("Game")]
        public RomInfo RomInfo;

        // TODO: get the rom associated with this game using gamedb
        public Rom Rom;

    // DV 2025-05-16: Why is this returning the name of the rom? Unused and confusing so commenting it out for now
        // public string Name {
        //     get => RomInfo.Name != "NULL" ? RomInfo.Name : name;
        // }
    }
}