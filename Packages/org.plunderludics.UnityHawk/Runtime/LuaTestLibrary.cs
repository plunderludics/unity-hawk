// // Example of how to define custom functions that can be called from lua

// using NLua;
// using BizHawk.Client.Common;

// using UnityEngine;
// using System;

// public sealed class LuaTestLibrary : LuaLibraryBase {
//     public LuaTestLibrary(ILuaLibraries luaLibsImpl, ApiContainer apiContainer, Action<string> logOutputCallback)
//     : base(luaLibsImpl, apiContainer, logOutputCallback) {}

//     public override string Name => "unityhawktest";

//     [LuaMethodExample("unityhawktest.sayhello( );")]
//     [LuaMethod("sayhello", "says hello")]
//     public void SayHello() {
//         Debug.Log("hello unity from lua");
//     }

//     [LuaMethodExample("local xxx = unityhawktest.gettestvalue( );")]
//     [LuaMethod("gettestvalue", "returns 123")]
//     public int GetTestValue() => 123;
// }