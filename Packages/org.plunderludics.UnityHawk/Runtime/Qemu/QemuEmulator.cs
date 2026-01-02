using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;
using System.Linq;
using System;
using UnityEngine;

using TriInspector;

namespace UnityHawk.QEMU {
public class QemuEmulator : MonoBehaviour
{
    Process _qemuProcess;
    QemuVncClient _vncClient;
    QemuQmpClient _qmpClient;
    
    [ShowInInspector] bool VncConnected => _vncClient != null && _vncClient.IsConnected;
    [ShowInInspector] bool VncInternalClientConnected => _vncClient != null && _vncClient.IsInternalClientConnected;

    public bool enableQmp = true;
    public bool showGui = false;
    public bool passKeyboardInputFromUnity = true;
    public bool passMouseInputFromUnity = true;

    // TODO I guess this should be an asset that gets imported, idk
    public string diskImagePath = "win95.qcow2"; // Relative to /Assets/

    public string saveStateName = "";

    [SerializeField] private int vncPort = 5900;
    [SerializeField] private int qmpPort = 4444;
    [SerializeField] private RenderTexture outputTexture; // This is kind of unnecessary should just use _vncClient.Texture directly, ideally..

    [TextArea(3, 10)]
    public string qemuArgs = @"
    -m 64
    -cpu pentium
    -vga cirrus
    -device sb16,audiodev=snd0
    -audiodev dsound,id=snd0
    -cdrom aoe-game.iso
    "; // VNC display args gets added automatically
    // TODO: move necessary+common args into separate fields



    public Texture2D Texture => _vncClient?.Texture;

    public int Width => _vncClient?.Texture?.width ?? -1;
    public int Height => _vncClient?.Texture?.height ?? -1;

    [Button]
    public async void Restart() {
        StopQemu();
        await StartQemuAsync();
    }

    async Task StartQemuAsync()
    {
        // Use Path.Combine to take advantage of unity's dark magic (somehow redirects to the actual package location in packagecache if needed)
        var qemuExe = Path.Combine("Packages", "org.plunderludics.UnityHawk", "qemu~", "qemu-system-i386.exe");
        qemuExe = Path.GetFullPath(qemuExe);
        UnityEngine.Debug.Log($"QEMU executable: {qemuExe}");

        var process = new Process();
        process.StartInfo.FileName = qemuExe;

        foreach (var arg in qemuArgs.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (showGui)
        {
            process.StartInfo.ArgumentList.Add("-display");
            process.StartInfo.ArgumentList.Add("sdl");
        }

        if (!string.IsNullOrEmpty(diskImagePath))
        {
            process.StartInfo.ArgumentList.Add("-hda");
            process.StartInfo.ArgumentList.Add(diskImagePath);
        }

        // Add VNC display - :0 means display 0, which is port 5900
        // Format: -display vnc=:0
        process.StartInfo.ArgumentList.Add("-display");
        process.StartInfo.ArgumentList.Add($"vnc=:{vncPort - 5900}");
        
        // Add QMP socket for command control
        // Format: -qmp tcp:host:port,server,nowait
        if (enableQmp) {
            process.StartInfo.ArgumentList.Add("-qmp");
            process.StartInfo.ArgumentList.Add($"tcp:127.0.0.1:{qmpPort},server,nowait");
        }
        
        // Redirect output to see if QEMU has any errors
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        UnityEngine.Debug.Log($"{qemuExe} {string.Join(' ', process.StartInfo.ArgumentList)}");

        process.Start();
        _qemuProcess = process;

        UnityEngine.Debug.Log($"Started QEMU process (PID: {process.Id}) with VNC on port {vncPort}");
        
        // Log any immediate errors from QEMU
        process.BeginErrorReadLine();
        process.OutputDataReceived += (sender, e) => {
            UnityEngine.Debug.Log($"QEMU output: {e.Data}");
        };
        process.ErrorDataReceived += (sender, e) => {
            UnityEngine.Debug.LogWarning($"QEMU error: {e.Data}");
        };

        // Wait a moment for QEMU to start and QMP socket to be ready
        await Task.Delay(1000);

        // Connect VNC client
        await ConnectVncAsync();
        
        if (enableQmp) {
            // Connect QMP client
            await ConnectQmpAsync();
        
            // If we have a save state file, load it via QMP
            if (!string.IsNullOrEmpty(saveStateName))
            {
                await LoadSaveStateAsync(saveStateName);
            }
        }
        
    }

    async void Start() {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        await StartQemuAsync();
    }

    void Update() {
        // Update VNC client (though it runs async, this ensures it's processing)
        if (_vncClient != null)
        {
            _vncClient.Update();
            
            // Copy VNC texture to render texture if both exist
            if (_vncClient.Texture != null && outputTexture != null)
            {
                Graphics.Blit(_vncClient.Texture, outputTexture);
            }

            // Handle input
            HandleInput();
        }
    }

    // TODO move into some kind of BasicInputProvider type of class
    void HandleInput()
    {
        if (!passKeyboardInputFromUnity && !passMouseInputFromUnity)
            return;

        if (_vncClient == null || _vncClient.Texture == null)
            return;

        if (passMouseInputFromUnity) {
            var texture = _vncClient.Texture;
            int vncWidth = texture.width;
            int vncHeight = texture.height;

            // Mouse input
            Vector3 mousePos = Input.mousePosition;
            
            // Convert Unity screen coordinates to VNC coordinates
            // Assuming the texture is displayed in a UI element or render texture
            // For now, map directly from screen space to VNC space
            // You may need to adjust this based on how you're displaying the texture


            int vncX = Mathf.Clamp((int)(mousePos.x * vncWidth / Screen.width), 0, vncWidth - 1);
            int vncY = Mathf.Clamp((int)(mousePos.y * vncHeight / Screen.height), 0, vncHeight - 1);
            
            // Flip Y coordinate (Unity has origin at bottom-left, VNC at top-left)
            vncY = vncHeight - 1 - vncY;

            bool leftButton = Input.GetMouseButton(0);
            bool middleButton = Input.GetMouseButton(2);
            bool rightButton = Input.GetMouseButton(1);
            
            SendMouseEvent(vncX, vncY, leftButton, middleButton, rightButton);
        }

        if (passKeyboardInputFromUnity) {
            // Keyboard input
            foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(key))
                {
                    SendKeyEvent(key, true);
                }
                if (Input.GetKeyUp(key))
                {
                    SendKeyEvent(key, false);
                }
            }
        }
    }

    // x and y are pixel coordinates from top-left, in actual display resolution (or does the VNC framebuffer have different resolution?)
    public void SendMouseEvent(int x, int y, bool leftButton, bool middleButton, bool rightButton) {
        if (_vncClient == null || _vncClient.Texture == null) {
            UnityEngine.Debug.LogWarning("VNC client not connected");
            return;
        }
        _vncClient.SendMouseEvent(x, y, leftButton, middleButton, rightButton);
    }

    public void SendKeyEvent(KeyCode key, bool down) {
        int keysym = UnityKeyCodeToVncKeysym(key);
        if (keysym == 0) {
            UnityEngine.Debug.LogWarning($"Unknown key: {key}");
            return;
        }
        if (_vncClient == null || _vncClient.Texture == null) {
            UnityEngine.Debug.LogWarning("VNC client not connected");
            return;
        }

        _vncClient.SendKeyEvent(keysym, down);
    }


    void OnDestroy()
    {
        StopQemu();
    }
    async Task ConnectVncAsync()
    {
        try
        {
            _vncClient = new QemuVncClient();
            await _vncClient.ConnectAsync("127.0.0.1", vncPort - 5900);
            
            if (outputTexture == null)
            {
                // Create default render texture if not assigned
                outputTexture = new RenderTexture(640, 480, 0);
                outputTexture.name = "QEMU Output";
            }
            
            UnityEngine.Debug.Log("VNC client connected!");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to connect VNC client: {e.Message}");
        }
    }

    async Task ConnectQmpAsync()
    {
        UnityEngine.Debug.Log($"Connecting QMP client to 127.0.0.1:{qmpPort}");
        try
        {
            _qmpClient = new QemuQmpClient();
            UnityEngine.Debug.Log($"Creating QMP client");
            await _qmpClient.ConnectAsync("127.0.0.1", qmpPort);
            UnityEngine.Debug.Log("QMP client connected!");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to connect QMP client: {e.Message}");
        }
    }

    async Task LoadSaveStateAsync(string saveStateName)
    {
        if (_qmpClient == null || !_qmpClient.IsConnected)
        {
            UnityEngine.Debug.LogError("Cannot load save state: QMP client not connected");
            return;
        }

        try
        {
            // Send snapshot-load command
            string argsJson = $@"{{
                ""job-id"": ""xxx"",
                ""tag"": ""{saveStateName}"",
                ""vmstate"": ""disk0"",
                ""devices"": [""disk0""]
            }}";
            var response = await _qmpClient.ExecuteCommandAsync("snapshot-load", argsJson);
            UnityEngine.Debug.Log($"Save state load initiated: {response}");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to load save state via QMP: {e.Message}");
        }
    }

    void StopQemu()
    {
        _vncClient?.Dispose();
        _vncClient = null;
        
        _qmpClient?.Dispose();
        _qmpClient = null;
        
        if (_qemuProcess != null && !_qemuProcess.HasExited)
        {
            _qemuProcess.Kill();
            _qemuProcess.WaitForExit();
            _qemuProcess.Dispose();
            _qemuProcess = null;
            UnityEngine.Debug.Log("Stopped QEMU process");
        }
    }

    
    /// <summary>
    /// Convert Unity KeyCode to VNC keysym
    /// This is a basic mapping - you may need to expand it for special keys
    /// VNC keysyms are X11 keysym values
    /// </summary>
    int UnityKeyCodeToVncKeysym(KeyCode key)
    {
        // Letters (A-Z)
        if (key >= KeyCode.A && key <= KeyCode.Z)
        {
            return 'a' + (key - KeyCode.A);
        }

        // Numbers (0-9)
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
        {
            return '0' + (key - KeyCode.Alpha0);
        }

        // Special keys - basic mapping
        switch (key)
        {
            case KeyCode.Space: return 0x0020; // Space
            case KeyCode.Return: return 0xFF0D; // Enter
            case KeyCode.Escape: return 0xFF1B; // Escape
            case KeyCode.Backspace: return 0xFF08; // Backspace
            case KeyCode.Tab: return 0xFF09; // Tab
            case KeyCode.LeftShift: return 0xFFE1; // Left Shift
            case KeyCode.RightShift: return 0xFFE2; // Right Shift
            case KeyCode.LeftControl: return 0xFFE3; // Left Ctrl
            case KeyCode.RightControl: return 0xFFE4; // Right Ctrl
            case KeyCode.LeftAlt: return 0xFFE9; // Left Alt
            case KeyCode.RightAlt: return 0xFFEA; // Right Alt
            case KeyCode.UpArrow: return 0xFF52; // Up
            case KeyCode.DownArrow: return 0xFF54; // Down
            case KeyCode.LeftArrow: return 0xFF51; // Left
            case KeyCode.RightArrow: return 0xFF53; // Right
            default: return 0; // Unknown key
        }
    }
}
}