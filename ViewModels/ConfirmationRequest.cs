namespace WolfEQ.ViewModels;

/// <summary>A question for the user, shown by the view layer in WolfEQ's own confirm modal.</summary>
public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText = "Continue",
    bool IsDestructive = false,
    string? Detail = null,
    string CancelText = "Cancel");
