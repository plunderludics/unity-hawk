using BizHawk.Client.Common;

class DialogParent : IDialogParent {
    public IDialogController DialogController { get; } = new DialogController();
}