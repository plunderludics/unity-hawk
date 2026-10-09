using System;
using SharedMemory;
using Plunderludics.UnityHawk.Shared;

namespace Plunderludics.BizHawk.Bridge {

public class CallMethodRpcBuffer : ISharedBuffer {
    string _name;
    RpcBuffer _rpcBuffer;

    public delegate void Callback(string methodName, string argString, out string output);
    Callback _callback;
    IBridgeLog _logger;

    public CallMethodRpcBuffer(string name, Callback callRegisteredMethod, IBridgeLog logger) {
        _name = name;
        _callback = callRegisteredMethod;
        _logger = logger ?? NullBridgeLog.Instance;
    }

    public void Open() {
        _rpcBuffer = new RpcBuffer(
            name: _name,
            (msgId, payload) => {
                string returnString;
                try {
                    _logger.LogVerbose($"callmethod rpc request {string.Join(", ", payload)}");
                    MethodCall methodCall = Serialization.RawDeserialize<MethodCall>(payload);
                    _callback(methodCall.MethodName, methodCall.Argument, out returnString);
                    if (returnString == null) {
                        _logger.LogWarning($"{methodCall.MethodName} returned null but null return values are not supported, converting to empty string");
                        returnString = "";
                    }
                } catch (Exception e) {
                    _logger.LogError(e.ToString());
                    returnString = "";
                }

                return System.Text.Encoding.ASCII.GetBytes(returnString);
            }
        );
    }

    public bool IsOpen() {
        return _rpcBuffer != null;
    }

    public void Close() {
        _rpcBuffer.Dispose();
        _rpcBuffer = null;
    }
}

}
