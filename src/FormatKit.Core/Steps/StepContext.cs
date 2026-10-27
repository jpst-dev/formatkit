namespace FormatKit.Core.Steps;

public sealed record StepContext(ServiceSet Services, Serilog.ILogger Log);
