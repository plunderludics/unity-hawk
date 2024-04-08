using UnityEngine;
using System;
using SharedMemory;
using BizHawk.UnityHawk;
using System.Runtime.InteropServices.WindowsRuntime;
public class CallMethodRpcBuffer : ISharedBuffer {
    private string _name;
    private RpcBuffer _rpcBuffer;
    private Func<string, string, string> _callRegisteredMethod;
    public CallMethodRpcBuffer(string name, Func<string, string, string> callRegisteredMethod) {
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

                    returnString = _callRegisteredMethod(methodName, argString);

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