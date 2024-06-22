using NaughtyAttributes;
using UnityEngine;

namespace UnityHawk {

public class Savestate : BizhawkAsset {
    public string Name;

    public string Hash;

    public string System;

    public string Region;

    public bool NotInDatabase;

    public string Core;

    // TODO: get the rom associated with this game using gamedb
    public Rom Rom;
}
}