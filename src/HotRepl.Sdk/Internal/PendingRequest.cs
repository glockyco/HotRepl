using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk.Internal;

internal sealed class PendingRequest : IDisposable
{
    private readonly CancellationTokenSource _timeout;

    public PendingRequest(TaskCompletionSource<JObject> completion, CancellationTokenSource timeout)
    {
        Completion = completion;
        _timeout = timeout;
    }

    public TaskCompletionSource<JObject> Completion { get; }

    public void Dispose()
    {
        _timeout.Dispose();
    }
}
