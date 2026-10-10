namespace WolfEQ.ViewModels;

/// <summary>
/// A request for a line of text (e.g. a new preset name), shown in WolfEQ's own modal.
/// <paramref name="Validate"/> returns an error message for invalid text, or null when it is fine.
/// </summary>
public sealed record TextPromptRequest(
    string Title,
    string Message,
    string InitialText,
    string ConfirmText = "Save",
    Func<string, string?>? Validate = null);
