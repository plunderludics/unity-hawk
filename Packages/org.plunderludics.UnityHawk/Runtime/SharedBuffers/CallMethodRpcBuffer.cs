// This is for BizHawk lua to call methods in Unity

using UnityEngine;
using System;
using SharedMemory;

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

                    // deserialize payload into method name and args (separated by 0)
                    // [could/should use MethodCall struct here + generic deserialization instead]
                    int methodNameLength = System.Array.IndexOf(payload, (byte)0);
                    byte[] methodNameBytes = new byte[methodNameLength];
                    byte[] argBytes = new byte[payload.Length - methodNameLength - 1];
                    Array.Copy(
                        sourceArray: payload,
                        sourceIndex: 0,
                        destinationArray: methodNameBytes,
                        destinationIndex: 0,
                        length: methodNameLength);
                    Array.Copy(
                        sourceArray: payload,
                        sourceIndex: methodNameLength + 1,
                        destinationArray: argBytes,
                        destinationIndex: 0,
                        length: argBytes.Length);

                    string methodName = System.Text.Encoding.ASCII.GetString(methodNameBytes);
                    string argString = System.Text.Encoding.ASCII.GetString(argBytes);

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