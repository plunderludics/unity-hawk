// This is for calls to Bizhawk that require a return value
// Warning: runs at an arbitrary time in a non-main thread
// Use ApiCommandBuffer for simple calls that don't require a return value - those will run in the main thread at the beginning of each frame

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;

using Plunderludics.UnityHawk.Shared;
using UnityEngine;
using SharedMemory;

namespace UnityHawk {
public class ApiCallRpcBuffer : ISharedBuffer {
    string _name;
    RpcBuffer _apiCallRpc;
    Logger _logger;

    const int TimeoutMs = 1000; // No point waiting more than a second I feel like

    public ApiCallRpcBuffer(string bufferName, Logger logger) {
        _name = bufferName;
        _logger = logger;
    }

    public void Open() {
        _apiCallRpc = new RpcBuffer(name: _name);
    }

    public string CallMethod(string methodName, string arg = null) {
        if (_apiCallRpc == null) {
            _logger.LogWarning($"Tried to call method {methodName} but the api call buffer is not yet open");
            return null;
        }
        // serialize (methodName, input) into a MethodCall struct
        MethodCall methodCall = new MethodCall {
            MethodName = methodName,
            Argument = arg
        };
        byte[] bytes = Serialization.Serialize(methodCall);

        _logger.LogVerbose($"Sending callmethod RPC request to Bizhawk ({methodName}, {arg})");
        // TODO async version of this?
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
        string responseString = System.Text.Encoding.ASCII.GetString(response.Data);
        return responseString;
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