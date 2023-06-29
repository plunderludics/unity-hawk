using BizHawk.Client.Common;

class UHDialogParent : IDialogParent {
    public IDialogController DialogController { get; } = new UHDialogController();
}