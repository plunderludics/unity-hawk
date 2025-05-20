using System.Collections.Generic;
using SharedMemory;
using Plunderludics.UnityHawk;

public class SharedAnalogInputBuffer : ISharedBuffer {
    private string _name;
    private SharedArray<byte> _buffer;
    public SharedAnalogInputBuffer(string name) {
        _name = name;
    }

    public void Open() {
        _buffer = new (_name);
    }

    public bool IsOpen() {
        return _buffer != null && _buffer.Length > 0;
    }

    public void Close() {
        _buffer.Close();
        _buffer = null;
    }

    // public void Write(Dictionary<string, int> axisValuesDict) {
    //     // Convert dictionary to array of AxisValue structs and write to buffer
    //     AxisValue[] axisValues = new AxisValue[InputStructConsts.AxisValueArrayLength]; // Have to send 256 elements because it's sent via IPC as constant-size buffer
    //     int i = 0;
    //     foreach (var kv in axisValuesDict) {
    //         axisValues[i].Name = kv.Key;
    //         axisValues[i].Value = kv.Value;
    //         i++;
    //     }
    //     AxisValuesStruct axisValuesStruct = new AxisValuesStruct {
    //         axisValues = axisValues
    //     };
    //     byte[] serialized = Serialization.Serialize(axisValuesStruct);
	// 	_buffer.Write(serialized, 0);
    // }
}