using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.Core;

namespace FormatKit.App.ViewModels;

public sealed record CliCommandInfo(string Command, string Description);

public sealed partial class AboutViewModel : ObservableObject
{
    private static readonly string[] CliCommandNames =
        ["cleanup undo", "drivers backup-network", "programs export", "programs search", "dev wsl-user", "dev ssh-key"];

    public AboutViewModel(IDialogService dialogs)
    {
        OpenLogFolderCommand = new RelayCommand(() => dialogs.OpenFolder(AppPaths.Default.LogsDirectory));
        CliCommands = CliCommandNames
            .Select(c => new CliCommandInfo($"formatkit {c}", Strings.Get("About_Cli_" + c.Replace(' ', '_').Replace('-', '_'))))
            .ToList();
    }

    public string Version { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public string RepositoryUrl => "https://github.com/jpst-dev/formatkit";

    public IReadOnlyList<CliCommandInfo> CliCommands { get; }

    public IRelayCommand OpenLogFolderCommand { get; }
}
