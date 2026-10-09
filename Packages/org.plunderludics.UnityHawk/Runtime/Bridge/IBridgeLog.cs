namespace Plunderludics.BizHawk.Bridge {

public interface IBridgeLog {
    void LogVerbose(string message);
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
}

public sealed class NullBridgeLog : IBridgeLog {
    public static readonly NullBridgeLog Instance = new NullBridgeLog();
    public void LogVerbose(string message) {}
    public void Log(string message) {}
    public void LogWarning(string message) {}
    public void LogError(string message) {}
}

}
