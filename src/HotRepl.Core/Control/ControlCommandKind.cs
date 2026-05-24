namespace HotRepl.Control;

/// <summary>Execution mode for a control-plane command.</summary>
public enum ControlCommandKind
{
    /// <summary>The command returns a result during the request/response round-trip.</summary>
    Sync,

    /// <summary>The command starts a cooperative job whose progress/result is queried separately.</summary>
    Job,
}
