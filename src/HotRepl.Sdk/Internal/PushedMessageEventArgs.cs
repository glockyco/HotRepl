using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk.Internal;

internal sealed class PushedMessageEventArgs : EventArgs
{
    public PushedMessageEventArgs(JObject message)
    {
        Message = message;
    }

    public JObject Message { get; }
}
