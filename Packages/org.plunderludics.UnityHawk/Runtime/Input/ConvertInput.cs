namespace UnityHawk {

internal static class ConvertInput {
    public static Plunderludics.UnityHawk.Shared.InputEvent ToBizHawk(InputEvent ie) {
        return Plunderludics.BizHawk.Bridge.InputConvert.ToBizHawk(ie.name, ie.value, (int)ie.controller, ie.isAnalog);
    }
}

}
