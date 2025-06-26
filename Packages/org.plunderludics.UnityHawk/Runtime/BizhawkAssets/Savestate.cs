using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityHawk {

public class Savestate : BizhawkAsset {
    [FormerlySerializedAs("GameInfo")]
    [Header("Game")]
    public RomInfo RomInfo;

    // TODO: get the rom associated with this game using gamedb
    // public Rom Rom;

    // public string Name {
    //     get => RomInfo.Name != "NULL" ? RomInfo.Name : name;
    // }

    public bool MatchesRom(Rom rom) {
        return (
            (!string.IsNullOrEmpty(rom?.Hash)
                && rom?.Hash == this.RomInfo.Hash)   // If both assets have a hash and they match
            || (string.IsNullOrEmpty(rom?.Hash)
                && this.RomInfo.Name == rom?.name)   // Fallback to name if rom has no hash
        );
    }
}
}