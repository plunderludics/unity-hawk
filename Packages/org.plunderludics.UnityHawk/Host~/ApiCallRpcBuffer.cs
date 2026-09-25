using SharedMemory;
using Plunderludics.UnityHawk.Shared;

namespace UnityHawk.Host {

public class ApiCallRpcBuffer : ISharedBuffer {
    string _name;
    RpcBuffer _apiCallRpc;
    IHostLog _logger;

    const int TimeoutMs = 1000;

    public ApiCallRpcBuffer(string bufferName, IHostLog logger) {
        _name = bufferName;
        _logger = logger ?? NullHostLog.Instance;
    }

    public void Open() {
        _apiCallRpc = new RpcBuffer(name: _name);
    }

    public string CallMethod(string methodName, string arg = null) {
        if (_apiCallRpc == null) {
            _logger.LogWarning($"Tried to call method {methodName} but the api call buffer is not yet open");
            return null;
        }
        MethodCall methodCall = new MethodCall {
            MethodName = methodName,
            Argument = arg
        };
        byte[] bytes = Serialization.Serialize(methodCall);

        _logger.LogVerbose($"Sending callmethod RPC request to Bizhawk ({methodName}, {arg})");
        var response = _apiCallRpc.RemoteRequest(bytes, TimeoutMs);
        if (response == null) {
            _logger.LogWarning($"Tried to call method {methodCall} but Bizhawk didn't respond");
            return null;
        }
        if (!response.Success) {
            _logger.LogWarning($"Bizhawk failed to return a value for callmethod {methodCall}");
            return null;
        }
        if (response.Data == null) {
            _logger.LogWarning($"Bizhawk returned an empty response for callmethod {methodCall}");
            return null;
        }
        return System.Text.Encoding.ASCII.GetString(response.Data);
    }

    public bool IsOpen() {
        return _apiCallRpc != null;
    }

    public void Close() {
        _apiCallRpc.Dispose();
        _apiCallRpc = null;
    }
}

}
