using UnityEngine;
using BizHawk.Client.Common;
using System.Collections.Generic;

class UHDialogController : IDialogController {
    public void AddOnScreenMessage(string message) {
        Debug.Log($"dialog controller OSM: {message}");
    }

    public IReadOnlyList<string>? ShowFileMultiOpenDialog(
    IDialogParent dialogParent,
    string? filterStr,
    ref int filterIndex,
    string initDir,
    bool discardCWDChange = false,
    string? initFileName = null,
    bool maySelectMultiple = false,
    string? windowTitle = null) {
        return new List<string>() {
            "test"
        };
    }

    public string? ShowFileSaveDialog(
        IDialogParent dialogParent,
        bool discardCWDChange,
        string? fileExt,
        string? filterStr,
        string initDir,
        string? initFileName,
        bool muteOverwriteWarning) {
            return "hello";
    }

    public void ShowMessageBox(
        IDialogParent? owner,
        string text,
        string? caption = null,
        EMsgBoxIcon? icon = null) {
                Debug.Log($"DialogController: {text}");
    }

    public bool ShowMessageBox2(
        IDialogParent? owner,
        string text,
        string? caption = null,
        EMsgBoxIcon? icon = null,
        bool useOKCancel = false) {
            Debug.Log($"DialogController: {text}");
            return true;
    }

    public bool? ShowMessageBox3(
        IDialogParent? owner,
        string text,
        string? caption = null,
        EMsgBoxIcon? icon = null) {
            Debug.Log($"DialogController: {text}");
            return true;
    }

    public void StartSound() {
        Debug.Log("Dialog Controller Starting Sound");
    }

    public void StopSound() {
        Debug.Log("Dialog Controller Stopping Sound");
    }

}