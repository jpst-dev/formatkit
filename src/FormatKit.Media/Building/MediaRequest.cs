using FormatKit.Media.Bundle;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Building;

/// <summary>What to build: the ISO, the setup choices, the optional profile bundle, the host locale and the executable to bundle.</summary>
public sealed record MediaRequest(string IsoPath, SetupOptions Setup, ProfileBundle? Bundle, MachineLocale Machine, string ExePath);
