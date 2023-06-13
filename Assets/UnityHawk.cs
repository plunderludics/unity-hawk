// global manager thing does one-time initialization of all the stuff BizHawk expects (db, etc)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;
using System.Runtime.InteropServices;

public class UnityHawk : MonoBehaviour
{   
    public static readonly string bizhawkDir = Path.Combine(Application.dataPath, "BizHawk");
    public static readonly string dllDir = Path.Combine(bizhawkDir, "dll");
    public static readonly string romsDir = Path.Combine(Application.dataPath, "Roms");

    void Awake()
    {
        // this will look in subdirectory "dll" to load pinvoked stuff
        Debug.Log($"Setting dll directory to {dllDir}");

        _ = SetDllDirectory(dllDir);

        // [copied from MainForm.cs]
        // Note: the gamedb directory MUST already have the gamedb.txt file, otherwise seems to hang :(
        string gamedbPath = Path.Combine(bizhawkDir, "gamedb");
        Database.InitializeDatabase(
            bundledRoot: gamedbPath,
            userRoot: gamedbPath,
            silent: true);
        BootGodDb.Initialize(gamedbPath);

        // this is only necessary for certain platforms so maybe should be in a 
        // 'InitializeMAMEMachineIfNeeded' method that clients can call, something like that
        MAMEMachineDB.Initialize(gamedbPath);
    }

	[DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetDllDirectory(string lpPathName);
}
