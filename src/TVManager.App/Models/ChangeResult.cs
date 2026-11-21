namespace TVManager.Models;

public sealed record SettingChange(string Key, string? PreviousValue, string NewValue);

public sealed record ChangeResult(
    bool Success,
    string Message,
    SettingChange? Undo = null,
    IReadOnlyList<string>? Preview = null);
