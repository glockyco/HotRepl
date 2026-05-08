using System;

namespace HotRepl.Engine.Commands;

internal sealed record SelectEvaluatorCmd(string Id, string Evaluator, Guid ConnectionId)
    : IEngineCommand;
