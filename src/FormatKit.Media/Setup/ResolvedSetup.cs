namespace FormatKit.Media.Setup;

/// <summary>The setup choices after defaults are filled in from the image and the host; input of the answer file builder.</summary>
public sealed record ResolvedSetup(
    SetupAccount Account,
    string? Edition,
    string? ComputerName,
    string Language,
    string Keyboard,
    string TimeZone);
