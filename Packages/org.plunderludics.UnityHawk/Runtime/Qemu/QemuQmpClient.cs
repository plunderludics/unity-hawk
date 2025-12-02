using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// QEMU Machine Protocol (QMP) client for sending commands to QEMU.
/// Handles connection, handshake, and command execution.
/// </summary>
namespace UnityHawk.QEMU {
public class QemuQmpClient : IDisposable
{
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private StreamReader _reader;
    private StreamWriter _writer;
    private bool _isConnected = false;
    private bool _capabilitiesNegotiated = false;
    private int _commandIdCounter = 1;

    /// <summary>
    /// Whether the client is connected to QEMU's QMP socket.
    /// </summary>
    public bool IsConnected => _isConnected && _tcpClient != null && _tcpClient.Connected;

    /// <summary>
    /// Connect to QEMU's QMP socket.
    /// </summary>
    /// <param name="host">Hostname or IP address (usually "127.0.0.1" for localhost)</param>
    /// <param name="port">QMP port number</param>
    public async Task ConnectAsync(string host, int port)
    {
        Debug.Log($"Connecting to QMP socket on {host}:{port}");
        try
        {
            Debug.Log($"Creating TCP client");
            _tcpClient = new TcpClient();
            Debug.Log($"Connecting to Tcp client on {host}:{port}");
            await _tcpClient.ConnectAsync(host, port);
            _stream = _tcpClient.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };
            _isConnected = true;

            // QEMU sends a greeting message immediately upon connection
            // We need to read it and then send qmp_capabilities
            Debug.Log($"Reading QMP greeting");
            string greeting = await _reader.ReadLineAsync();
            Debug.Log($"QMP greeting: {greeting}");

            // Parse greeting to verify it's QMP
            if (!string.IsNullOrEmpty(greeting))
            {
                JObject greetingObj = JObject.Parse(greeting);
                if (greetingObj["QMP"] != null)
                {
                    // Send qmp_capabilities to complete handshake
                    await NegotiateCapabilitiesAsync();
                }
                else
                {
                    throw new Exception("Invalid QMP greeting - expected QMP property");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect to QMP socket: {e.Message}");
            _isConnected = false;
            throw;
        }
    }

    /// <summary>
    /// Negotiate QMP capabilities (required handshake step).
    /// </summary>
    private async Task NegotiateCapabilitiesAsync()
    {
        var response = await ExecuteCommandAsync("qmp_capabilities");
        
        if (response["return"] != null)
        {
            _capabilitiesNegotiated = true;
            Debug.Log("QMP capabilities negotiated successfully");
        }
        else if (response["error"] != null)
        {
            throw new Exception($"Failed to negotiate QMP capabilities: {response.ToString()}");
        }
    }

    /// <summary>
    /// Execute a QMP command and return the response.
    /// </summary>
    /// <param name="command">QMP command name (e.g., "stop", "cont", "query-status")</param>
    /// <param name="arguments">Optional command arguments as a JObject</param>
    /// <returns>JSON response as JObject</returns>
    async Task<JObject> ExecuteCommandAsync(string command, JObject arguments)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to QMP socket");
        }

        if (!_capabilitiesNegotiated && command != "qmp_capabilities")
        {
            throw new InvalidOperationException("QMP capabilities not negotiated. Call ConnectAsync first.");
        }

        int commandId = _commandIdCounter++;
        
        // Build command JSON
        JObject commandObj = new JObject
        {
            ["execute"] = command,
            ["id"] = commandId
        };
        
        if (arguments != null)
        {
            commandObj["arguments"] = arguments;
        }

        string commandJson = commandObj.ToString(Newtonsoft.Json.Formatting.None);
        Debug.Log($"Sending QMP command: {commandJson}");
        await _writer.WriteLineAsync(commandJson);
        Debug.Log($"QMP command sent: {commandJson}");

        // Read response
        string responseLine = await _reader.ReadLineAsync();
        if (string.IsNullOrEmpty(responseLine))
        {
            throw new Exception("Empty response from QMP");
        }

        Debug.Log($"QMP response: {responseLine}");

        JObject response = JObject.Parse(responseLine);

        // Check for error
        if (response["error"] != null)
        {
            JToken error = response["error"];
            string errorClass = error["class"]?.ToString() ?? "Unknown";
            string errorDesc = error["desc"]?.ToString() ?? "Unknown error";
            throw new Exception($"QMP command failed: {errorClass} - {errorDesc}");
        }

        // Verify command ID matches
        if (response["id"] != null && response["id"].Value<int>() != commandId)
        {
            Debug.LogWarning($"QMP response ID mismatch: expected {commandId}, got {response["id"].Value<int>()}");
        }

        return response;
    }

    /// <summary>
    /// Execute a QMP command with arguments as a JSON string.
    /// </summary>
    /// <param name="command">QMP command name</param>
    /// <param name="argumentsJson">JSON string containing the arguments object, or null/empty for no arguments</param>
    public async Task<JObject> ExecuteCommandAsync(string command, string argumentsJson = null)
    {
        JObject arguments = string.IsNullOrEmpty(argumentsJson) ? null : JObject.Parse(argumentsJson);
        return await ExecuteCommandAsync(command, arguments);
    }

    public void Dispose()
    {
        _isConnected = false;
        _capabilitiesNegotiated = false;

        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();

        _reader = null;
        _writer = null;
        _stream = null;
        _tcpClient = null;
    }
}
}