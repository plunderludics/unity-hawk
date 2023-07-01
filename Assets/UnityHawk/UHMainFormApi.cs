
using UnityEngine;
using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using System.Drawing;

// Nothing here is implemented right now, but some pieces could be
public class UHMainFormApi : IMainFormForApi
{
    public IMovieSession MovieSession {
        get {
            Debug.LogWarning("get__MovieSession not implemented, returning default");
            return default;
        }
    }

    public GameInfo Game => throw new System.NotImplementedException();

    public CheatCollection CheatList => throw new System.NotImplementedException();

    public Point DesktopLocation => throw new System.NotImplementedException();

    public IEmulator Emulator => throw new System.NotImplementedException();

    public bool EmulatorPaused => throw new System.NotImplementedException();

    public bool InvisibleEmulation { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public bool IsSeeking => throw new System.NotImplementedException();

    public bool IsTurboing => throw new System.NotImplementedException();

    public (HttpCommunication HTTP, MemoryMappedFiles MMF, SocketServer Sockets) NetworkingHelpers {
        get {
            Debug.LogWarning("get__NetworkingHelpers not implemented, returning default");
            return default;
        }
    }

    public bool PauseAvi { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public void ClearHolds()
    {
        throw new System.NotImplementedException();
    }

    public void ClickSpeedItem(int num)
    {
        throw new System.NotImplementedException();
    }

    public void CloseEmulator(int? exitCode = null)
    {
        throw new System.NotImplementedException();
    }

    public void CloseRom(bool clearSram = false)
    {
        throw new System.NotImplementedException();
    }

    public void EnableRewind(bool enabled)
    {
        throw new System.NotImplementedException();
    }

    public bool FlushSaveRAM(bool autosave = false)
    {
        throw new System.NotImplementedException();
    }

    public void FrameAdvance()
    {
        throw new System.NotImplementedException();
    }

    public void FrameBufferResized()
    {
        throw new System.NotImplementedException();
    }

    public void FrameSkipMessage()
    {
        throw new System.NotImplementedException();
    }

    public int GetApproxFramerate()
    {
        throw new System.NotImplementedException();
    }

    public bool LoadMovie(string filename, string archive = null)
    {
        throw new System.NotImplementedException();
    }

    public bool LoadQuickSave(int slot, bool suppressOSD = false)
    {
        throw new System.NotImplementedException();
    }

    public bool LoadRom(string path, LoadRomArgs args)
    {
        throw new System.NotImplementedException();
    }

    public bool LoadState(string path, string userFriendlyStateName, bool suppressOSD = false)
    {
        throw new System.NotImplementedException();
    }

    public void PauseEmulator()
    {
        throw new System.NotImplementedException();
    }

    public bool RebootCore()
    {
        throw new System.NotImplementedException();
    }

    public void Render()
    {
        throw new System.NotImplementedException();
    }

    public bool RestartMovie()
    {
        throw new System.NotImplementedException();
    }

    public void SaveQuickSave(int slot, bool suppressOSD = false, bool fromLua = false)
    {
        throw new System.NotImplementedException();
    }

    public void SaveState(string path, string userFriendlyStateName, bool fromLua = false, bool suppressOSD = false)
    {
        throw new System.NotImplementedException();
    }

    public void SeekFrameAdvance()
    {
        throw new System.NotImplementedException();
    }

    public void SetVolume(int volume)
    {
        throw new System.NotImplementedException();
    }

    public void StepRunLoop_Throttle()
    {
        throw new System.NotImplementedException();
    }

    public void StopMovie(bool saveChanges = true)
    {
        throw new System.NotImplementedException();
    }

    public void TakeScreenshot()
    {
        throw new System.NotImplementedException();
    }

    public void TakeScreenshot(string path)
    {
        throw new System.NotImplementedException();
    }

    public void TakeScreenshotToClipboard()
    {
        throw new System.NotImplementedException();
    }

    public void TogglePause()
    {
        throw new System.NotImplementedException();
    }

    public void ToggleSound()
    {
        throw new System.NotImplementedException();
    }

    public void UnpauseEmulator()
    {
        throw new System.NotImplementedException();
    }
}