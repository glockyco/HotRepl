using System;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Sdk.Internal;

internal interface IDuplexFrameChannel : IAsyncDisposable
{
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    Task SendAsync(string json, CancellationToken cancellationToken);
}
