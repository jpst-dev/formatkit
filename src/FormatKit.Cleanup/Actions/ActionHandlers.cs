using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;

namespace FormatKit.Cleanup.Actions;

/// <param name="formatKitSchemes">The created schemes that are FormatKit performance plans; none when omitted.</param>
internal sealed class ActionHandlers(Func<IReadOnlyDictionary<Guid, Guid?>> createdSchemes, Func<IReadOnlySet<Guid>>? formatKitSchemes = null)
{
    private readonly RegistryHandler _registry = new();
    private readonly ServiceHandler _service = new();
    private readonly AppxHandler _appx = new();
    private readonly PowerSchemeHandler _power = new(createdSchemes, formatKitSchemes);
    private readonly OptionalFeatureHandler _feature = new();
    private readonly ScheduledTaskHandler _scheduledTask = new();
    private readonly StartupItemHandler _startupItem = new();
    private readonly PowerSettingHandler _powerSetting = new();
    private readonly MemoryIntegrityHandler _memoryIntegrity = new();

    public IActionHandler For(CleanupAction action) => action switch
    {
        RegistryAction => _registry,
        ServiceAction => _service,
        AppxAction => _appx,
        PowerSchemeAction => _power,
        OptionalFeatureAction => _feature,
        ScheduledTaskAction => _scheduledTask,
        StartupItemAction => _startupItem,
        PowerSettingAction => _powerSetting,
        NamedAction { Handler: MemoryIntegrityHandler.Name } => _memoryIntegrity,
        NamedAction named => throw new ArgumentException($"Unknown named action handler '{named.Handler}'.", nameof(action)),
        _ => throw new ArgumentException($"Unsupported action type '{action.GetType().Name}'.", nameof(action)),
    };

    public IActionHandler For(JournalAction recorded) => recorded.Type switch
    {
        RegistryHandler.Type => _registry,
        ServiceHandler.Type => _service,
        AppxHandler.Type => _appx,
        PowerSchemeHandler.Type => _power,
        OptionalFeatureHandler.Type => _feature,
        ScheduledTaskHandler.Type => _scheduledTask,
        StartupItemHandler.Type => _startupItem,
        PowerSettingHandler.Type => _powerSetting,
        MemoryIntegrityHandler.Name => _memoryIntegrity,
        _ => throw new ArgumentException($"Unknown journal action type '{recorded.Type}'.", nameof(recorded)),
    };
}
