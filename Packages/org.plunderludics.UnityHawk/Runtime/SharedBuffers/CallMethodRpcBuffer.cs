// This is for BizHawk lua to call methods in Unity

using UnityEngine;
using System;
using SharedMemory;

using Plunderludics.UnityHawk;

namespace UnityHawk {

public class CallMethodRpcBuffer : ISharedBuffer {
    /// the buffer name
    string _name;

    /// the rpc buffer
    RpcBuffer _rpcBuffer;

    /// the callback for this rpc
    public delegate bool CallRegisteredMethod(string methodName, string argString, out string output);
    CallRegisteredMethod _callRegisteredMethod;

    public CallMethodRpcBuffer(string name, CallRegisteredMethod callRegisteredMethod) {
        _name = name;
        _callRegisteredMethod = callRegisteredMethod;
    }

    public void Open() {
        _rpcBuffer = new (
            name: _name,
            (msgId, payload) => {
                string returnString;
                try { // Because this runs outside the main threads, we have to catch all exceptions to force them to display in the console
                    // Debug.Log($"callmethod rpc request {string.Join(", ", payload)}");

                    // Deserialize the payload to a MethodCall struct
                    MethodCall methodCall = Serialization.RawDeserialize<MethodCall>(payload);
                    string methodName = methodCall.MethodName;
                    string argString = methodCall.Argument;

                    var exists = _callRegisteredMethod(methodName, argString, out returnString);

                    // [messy hack] don't allow returning null because it seems to break things on the other side of the RPC
                    if (returnString == null) {
                        Debug.LogWarning($"{methodName} returned null but null return values are not supported, converting to empty string");
                        returnString = "";
                    }
                } catch (Exception e) {
                    Debug.LogException(e);
                    returnString = ""; // return an empty string to avoid crashing bizhawk
                }

                byte[] returnData = System.Text.Encoding.ASCII.GetBytes(returnString);
                return returnData;
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