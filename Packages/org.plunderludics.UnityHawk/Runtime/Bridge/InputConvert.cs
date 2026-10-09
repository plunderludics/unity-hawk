using Plunderludics.UnityHawk.Shared;

namespace Plunderludics.BizHawk.Bridge {

public static class InputConvert {
    public static InputEvent ToBizHawk(string name, int value, int controller, bool isAnalog) {
        return new InputEvent {
            name = name,
            value = value,
            controller = controller,
            isAnalog = isAnalog
        };
    }
}

}
