namespace FormatKit.Dev;

/// <summary>Where the tools the dev steps call usually live, for when they are not yet on this process's PATH.</summary>
internal static class KnownTools
{
    public static readonly string[] Git =
    [
        @"%ProgramFiles%\Git\cmd\git.exe",
        @"%LOCALAPPDATA%\Programs\Git\cmd\git.exe",
    ];

    public static readonly string[] SshKeygen = [@"%SystemRoot%\System32\OpenSSH\ssh-keygen.exe"];

    public static readonly string[] SshAdd = [@"%SystemRoot%\System32\OpenSSH\ssh-add.exe"];

    /// <summary>Oh My Posh is an MSIX package; the alias in WindowsApps is where it lives (spec 12), the rest are older installs.</summary>
    public static readonly string[] OhMyPosh =
    [
        @"%LOCALAPPDATA%\Microsoft\WindowsApps\oh-my-posh.exe",
        @"%LOCALAPPDATA%\Programs\oh-my-posh\bin\oh-my-posh.exe",
        @"%ProgramFiles%\oh-my-posh\bin\oh-my-posh.exe",
        @"%ProgramFiles(x86)%\oh-my-posh\bin\oh-my-posh.exe",
    ];

    /// <summary>What Git is told to use as ssh, so it talks to the Windows ssh-agent.</summary>
    public const string WindowsSsh = "C:/Windows/System32/OpenSSH/ssh.exe";
}
