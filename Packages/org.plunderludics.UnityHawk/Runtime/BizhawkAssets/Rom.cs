namespace UnityHawk {

public class Rom : BizhawkAsset {
    /// Store this to allow filtering savestate picker based on rom
    /// TODO: should we find a way to store <see cref="RomInfo" /> here instead
    public string Hash;
}

}