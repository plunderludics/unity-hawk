// A way of ensuring the assets needed for tests can be referenced in the build

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk.Tests {

public class SharedTestAssets : MonoBehaviour
{
    public Rom eliteRom;
    public Rom swoopRom;
    public Savestate eliteSavestate2000;
    public Savestate eliteSavestate5000;
    public LuaScript testCallbacksLua;
}

}
