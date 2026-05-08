using System;
using HotRepl.Protocol;

namespace HotRepl.Engine.Commands;

internal sealed record CommandCallCmd(CommandCallMessage Message, Guid ConnectionId)
    : IEngineCommand
{
    public string Id => Message.Id;
}
