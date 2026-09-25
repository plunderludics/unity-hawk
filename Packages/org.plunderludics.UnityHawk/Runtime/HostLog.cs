namespace UnityHawk {

sealed class HostLog : Host.IHostLog {
    readonly Logger _logger;

    public HostLog(Logger logger) {
        _logger = logger;
    }

    public void LogVerbose(string message) => _logger.LogVerbose(message);
    public void Log(string message) => _logger.Log(message);
    public void LogWarning(string message) => _logger.LogWarning(message);
    public void LogError(string message) => _logger.LogError(message);
}

}
