namespace FormatKit.App.Worker;

/// <summary>
/// The worker's media job: <c>media --iso &lt;path&gt; [--profile &lt;path&gt; --profile-hash &lt;hash&gt;]</c> with either
/// <c>--output &lt;folder&gt;</c> or <c>--usb [--allow-hard-disk]</c>. A null <see cref="Output"/> means the USB stick.
/// </summary>
public sealed record MediaJobRequest(string Iso, string? Profile, string? ProfileHash, string? Output, bool AllowHardDisk)
{
    public const string JobName = "media";

    public IReadOnlyList<string> ToArguments()
    {
        List<string> args = [JobName, "--iso", Iso];
        if (Profile is not null)
            args.AddRange(["--profile", Profile, "--profile-hash", ProfileHash!]);
        if (Output is not null)
            args.AddRange(["--output", Output]);
        else
            args.Add("--usb");
        if (AllowHardDisk)
            args.Add("--allow-hard-disk");
        return args;
    }

    /// <summary>The request, or null when the arguments are not exactly a valid media job.</summary>
    public static MediaJobRequest? Parse(IReadOnlyList<string> job)
    {
        if (job.Count == 0 || job[0] != JobName) return null;

        string? iso = null, profile = null, hash = null, output = null;
        bool usb = false, allowHardDisk = false;
        for (var i = 1; i < job.Count; i++)
        {
            switch (job[i])
            {
                case "--iso" when iso is null && i + 1 < job.Count: iso = job[++i]; break;
                case "--profile" when profile is null && i + 1 < job.Count: profile = job[++i]; break;
                case "--profile-hash" when hash is null && i + 1 < job.Count: hash = job[++i]; break;
                case "--output" when output is null && i + 1 < job.Count: output = job[++i]; break;
                case "--usb" when !usb: usb = true; break;
                case "--allow-hard-disk" when !allowHardDisk: allowHardDisk = true; break;
                default: return null;
            }
        }

        if (iso is null || usb == (output is not null) || (allowHardDisk && !usb) || (profile is null) != (hash is null))
            return null;
        return new MediaJobRequest(iso, profile, hash, output, allowHardDisk);
    }
}
