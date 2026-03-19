using UnityEngine;
using UnityHawk.QEMU;
using System.Text;
using TriInspector;

namespace UnityHawk {
public class QemuMemViewer : MonoBehaviour
{
    public QemuEmulator qemu;
    public long startAddress = 0x0;
    public int length = 256;
    public int bytesPerRow = 16;
    
    [ReadOnly, TextArea(10, 20)]
    public string memoryHex;

    void Update()
    {
        if (qemu == null) return;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            if (i > 0 && i % bytesPerRow == 0)
            {
                sb.AppendLine();
            }

            // Read byte directly using our new POC API
            uint val = qemu.ReadUnsigned(startAddress + i, 1, false);
            Debug.Log($"Read {val} from 0x{startAddress + i:X}");
            sb.Append(val.ToString("X2") + " ");
        }

        memoryHex = sb.ToString();
    }
}
}
