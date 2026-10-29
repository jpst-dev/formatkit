using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeVirtualDisks : IVirtualDisks
{
    public Dictionary<string, VirtualDiskState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = [];
    public HashSet<char> LettersInUse { get; } = [];
    public long FreeBytesValue { get; set; } = long.MaxValue;

    /// <summary>When set, <see cref="CreateDevDriveAsync"/> throws it instead of creating the disk.</summary>
    public Exception? CreateFailure { get; set; }

    /// <summary>When set, <see cref="RegisterRemountTaskAsync"/> throws it instead of registering.</summary>
    public Exception? RemountFailure { get; set; }

    public Task<VirtualDiskState?> GetAsync(string vhdxPath, CancellationToken ct)
    {
        Calls.Add($"Get {vhdxPath}");
        return Task.FromResult(States.TryGetValue(vhdxPath, out var state) ? state : null);
    }

    public Task AttachAsync(string vhdxPath, CancellationToken ct)
    {
        Calls.Add($"Attach {vhdxPath}");
        if (VanishOnAttach)
        {
            States.Remove(vhdxPath);
            return Task.CompletedTask;
        }

        if (States.TryGetValue(vhdxPath, out var state))
        {
            States[vhdxPath] = state with { Attached = true };
        }

        return Task.CompletedTask;
    }

    public Task CreateDevDriveAsync(string vhdxPath, int sizeGB, char letter, string label, CancellationToken ct)
    {
        Calls.Add($"Create {vhdxPath} {sizeGB} {letter} {label}");
        if (CreateFailure is not null)
        {
            throw CreateFailure;
        }

        States[vhdxPath] = new VirtualDiskState(true, letter, label);
        return Task.CompletedTask;
    }

    public Task DetachAndDeleteAsync(string vhdxPath, CancellationToken ct)
    {
        Calls.Add($"Delete {vhdxPath}");
        States.Remove(vhdxPath);
        return Task.CompletedTask;
    }

    public bool IsLetterInUse(char letter) => LettersInUse.Contains(char.ToUpperInvariant(letter));

    /// <summary>When set, <see cref="FreeBytes"/> throws it, as DriveInfo does for a missing or not-ready drive.</summary>
    public Exception? FreeBytesFailure { get; set; }

    /// <summary>When set, <see cref="AttachAsync"/> leaves the VHDX unknown afterwards (the file vanished).</summary>
    public bool VanishOnAttach { get; set; }

    public long FreeBytes(string hostRoot) => FreeBytesFailure is null ? FreeBytesValue : throw FreeBytesFailure;

    public Task RegisterRemountTaskAsync(string vhdxPath, CancellationToken ct)
    {
        Calls.Add($"RegisterRemount {vhdxPath}");
        if (RemountFailure is not null)
        {
            throw RemountFailure;
        }

        return Task.CompletedTask;
    }
}
