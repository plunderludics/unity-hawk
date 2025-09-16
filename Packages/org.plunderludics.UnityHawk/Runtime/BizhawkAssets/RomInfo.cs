using System;
using BizHawk.Emulation.Common;

namespace UnityHawk {

[Serializable]
public struct RomInfo {
    public string Name;

    public string Hash;

    public string System;

    public string Region;

    public bool NotInDatabase;

    public string Core;

    public RomInfo(GameInfo gameInfo) {
        Name = gameInfo.Name;
        Hash = gameInfo.Hash;
        Region = gameInfo.Region;
        System = gameInfo.System;
        NotInDatabase = gameInfo.NotInDatabase;
        Core = gameInfo.ForcedCore;
    }
}
}