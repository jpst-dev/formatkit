using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

/// <summary>Returns scripted results in order, repeating the last one when exhausted.</summary>
public sealed class FakeNetworkProbe : INetworkProbe
{
    private readonly Queue<bool> _results;
    private bool _last;

    public FakeNetworkProbe(params bool[] results)
    {
        _results = new Queue<bool>(results);
        _last = results.Length > 0 && results[^1];
    }

    public int CallCount { get; private set; }

    public Task<bool> IsOnlineAsync(CancellationToken ct)
    {
        CallCount++;
        if (_results.Count > 0)
        {
            _last = _results.Dequeue();
        }

        return Task.FromResult(_last);
    }
}
