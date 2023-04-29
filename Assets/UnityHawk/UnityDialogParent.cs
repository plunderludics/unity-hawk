using BizHawk.Client.Common;

class UnityDialogParent : IDialogParent {
    public IDialogController DialogController { get; } = new UnityDialogController();
}