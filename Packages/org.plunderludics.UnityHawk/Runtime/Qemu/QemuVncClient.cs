using System;
using System.Threading.Tasks;
using UnityEngine;
using RemoteViewing.Vnc;
using TriInspector;
using System.Diagnostics;

/// <summary>
/// VNC client wrapper using RemoteViewing library for QEMU
/// </summary>
namespace UnityHawk.QEMU {
public class QemuVncClient : IDisposable
{
    private string _host;
    private int _display;
    private VncClient _vncClient;
    private Texture2D _texture;
    private bool _connected = false;
    private bool _needsUpdate = false;
    private object _updateLock = new object();

    public bool IsConnected => _connected && _vncClient != null && _vncClient.IsConnected;
    public bool IsInternalClientConnected => _vncClient != null && _vncClient.IsConnected;
    public Texture2D Texture => _texture;

    public async Task ConnectAsync(string host, int display)
    {
        _host = host;
        _display = display;
        try
        {
            int port = 5900 + display;
            
            _vncClient = new VncClient();
            
            // Set up framebuffer changed event to update texture
            _vncClient.FramebufferChanged += OnFramebufferChanged;
            
            // Set up connection options
            var options = new VncClientConnectOptions
            {
                ShareDesktop = true,
                PixelFormat = new VncPixelFormat(
                    bitsPerPixel: 32,
                    bitDepth: 24,
                    redBits: 8,
                    redShift: 16,
                    greenBits: 8,
                    greenShift: 8,
                    blueBits: 8,
                    blueShift: 0,
                    isLittleEndian: true,
                    isPalettized: false
                )
            };
            
            // Connect to VNC server (synchronous method, run in task)
            await Task.Run(() => _vncClient.Connect(host, port, options));
            
            _connected = true;
            
            // Texture will be created on main thread when first framebuffer update arrives
            UnityEngine.Debug.Log($"VNC connected! Resolution: {_vncClient.Framebuffer?.Width ?? 0}x{_vncClient.Framebuffer?.Height ?? 0}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"VNC connection error: {e.GetType().Name}: {e.Message}");
            _connected = false;
            throw;
        }
    }

    private void OnFramebufferChanged(object sender, FramebufferChangedEventArgs e)
    {
        // Mark that we need an update - actual texture update happens on main thread
        lock (_updateLock)
        {
            _needsUpdate = true;
        }
    }
    
    /// <summary>
    /// Call this from Unity's Update() on the main thread to process framebuffer updates
    /// </summary>
    public void UpdateTexture()
    {
        if (!_needsUpdate || _vncClient?.Framebuffer == null)
            return;
        
        lock (_updateLock)
        {
            if (!_needsUpdate)
                return;
            
            _needsUpdate = false;
        }
        
        var framebuffer = _vncClient.Framebuffer;
        
        // Ensure texture size matches (this must be on main thread)
        if (_texture == null || _texture.width != framebuffer.Width || _texture.height != framebuffer.Height)
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
            }
            _texture = new Texture2D(framebuffer.Width, framebuffer.Height, TextureFormat.RGB24, false);
        }
        
        // Get pixel data from framebuffer
        // VncFramebuffer.GetPixels() returns int[] where each int represents a pixel
        var pixels = framebuffer.GetPixels();
        
        // Convert to Color32 array
        // RemoteViewing returns pixels as int[] where each int is ARGB (32-bit)
        Color32[] colors = new Color32[framebuffer.Width * framebuffer.Height];
        
        for (int y = 0; y < framebuffer.Height; y++)
        {
            for (int x = 0; x < framebuffer.Width; x++)
            {
                int pixelIndex = y * framebuffer.Width + x;
                int textureIndex = ((framebuffer.Height - 1 - y) * framebuffer.Width) + x; // Flip Y for Unity
                
                if (pixelIndex < pixels.Length)
                {
                    // Extract ARGB components from int (assuming little-endian ARGB format)
                    int pixel = pixels[pixelIndex];
                    byte a = (byte)((pixel >> 24) & 0xFF);
                    byte r = (byte)((pixel >> 16) & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)(pixel & 0xFF);
                    
                    colors[textureIndex] = new Color32(r, g, b, a == 0 ? (byte)255 : a);
                }
            }
        }
        
        // Update texture (must be on main thread)
        _texture.SetPixels32(colors);
        _texture.Apply();
    }

    public void Update()
    {
        // Update texture on main thread when framebuffer changes
        UpdateTexture();

        if (_vncClient != null && !_vncClient.IsConnected)
        {
            // Attempt to reconnect
            // UnityEngine.Debug.LogWarning("VNC client disconnected, attempting to reconnect");
            // ConnectAsync(_host, _display);
        }
    }

    /// <summary>
    /// Send mouse pointer event to QEMU via VNC
    /// </summary>
    /// <param name="x">X coordinate in VNC framebuffer space (0 to framebuffer width)</param>
    /// <param name="y">Y coordinate in VNC framebuffer space (0 to framebuffer height)</param>
    /// <param name="leftButton">Left mouse button pressed</param>
    /// <param name="middleButton">Middle mouse button pressed</param>
    /// <param name="rightButton">Right mouse button pressed</param>
    public void SendMouseEvent(int x, int y, bool leftButton, bool middleButton, bool rightButton)
    {
        if (!IsConnected || _vncClient == null)
            return;

        try
        {
            // VNC button mask: 1 = left, 2 = middle, 4 = right
            byte buttonMask = 0;
            if (leftButton) buttonMask |= 1;
            if (middleButton) buttonMask |= 2;
            if (rightButton) buttonMask |= 4;

            _vncClient.SendPointerEvent(x, y, buttonMask);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to send mouse event: {e.Message}");
        }
    }

    /// <summary>
    /// Send keyboard event to QEMU via VNC
    /// </summary>
    /// <param name="keysym">VNC keysym (key symbol) - see VNC keysym definitions</param>
    /// <param name="pressed">True for key press, false for key release</param>
    public void SendKeyEvent(int keysym, bool pressed)
    {
        if (!IsConnected || _vncClient == null)
            return;

        try
        {
            _vncClient.SendKeyEvent(keysym, pressed);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to send key event: {e.Message}");
        }
    }

    public void Dispose()
    {
        UnityEngine.Debug.LogWarning("Disposing VNC client");
        _connected = false;
        
        if (_vncClient != null)
        {
            _vncClient.FramebufferChanged -= OnFramebufferChanged;
            _vncClient.Close();
            _vncClient = null;
        }
    }
}

// Simple main thread dispatcher for Unity
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private System.Collections.Generic.Queue<System.Action> _queue = new System.Collections.Generic.Queue<System.Action>();
    
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    void Update()
    {
        lock (_queue)
        {
            while (_queue.Count > 0)
            {
                _queue.Dequeue()?.Invoke();
            }
        }
    }
    
    public void Enqueue(System.Action action)
    {
        lock (_queue)
        {
            _queue.Enqueue(action);
        }
    }
}
}