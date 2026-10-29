namespace FormatKit.Media.Setup;

public enum SetupAccount { Local, Microsoft }

/// <summary>The profile's top-level <c>setup</c> section: choices for the unattended Windows setup.</summary>
public sealed record SetupOptions(
    SetupAccount Account = SetupAccount.Local,
    string? Edition = null,
    string? ComputerName = null,
    string? Language = null,
    string? Keyboard = null,
    string? TimeZone = null);
