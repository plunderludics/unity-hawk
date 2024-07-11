using UnityEngine;

namespace UnityHawk.Samples {

[ExecuteInEditMode]
public class RegisterMethodExample : MonoBehaviour
{
    [SerializeField] Emulator e;

    // Start is called before the first frame update
    void OnEnable()
    {
        e.RegisterLuaCallback("DoSomething", DoSomething);
    }

    static string DoSomething(string arg) {
        var charArr = arg.ToCharArray();
        charArr[0] = '_';
        return new string(charArr);
    }
}

}