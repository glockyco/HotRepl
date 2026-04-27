using System;

namespace HotRepl.Evaluator;

internal sealed class SelectEvaluatorCmd : IEngineCommand
{
    public string Id { get; }
    public string Evaluator { get; }
    public Guid ConnectionId { get; }

    public SelectEvaluatorCmd(string id, string evaluator, Guid connectionId)
    {
        Id = id;
        Evaluator = evaluator;
        ConnectionId = connectionId;
    }
}
