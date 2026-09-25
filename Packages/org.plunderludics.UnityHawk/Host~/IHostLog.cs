namespace UnityHawk.Host {

public interface IHostLog {
    void LogVerbose(string message);
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
}

public sealed class NullHostLog : IHostLog {
    public static readonly NullHostLog Instance = new NullHostLog();
    public void LogVerbose(string message) {}
    public void Log(string message) {}
    public void LogWarning(string message) {}
    public void LogError(string message) {}
}

}
