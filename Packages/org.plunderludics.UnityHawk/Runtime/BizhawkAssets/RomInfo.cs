using System;

namespace UnityHawk {
    [Serializable]
    public struct RomInfo {
        public string Name;

        public string Hash;

        public string System;

        public string Region;

        public bool NotInDatabase;

        public string Core;
    }
}