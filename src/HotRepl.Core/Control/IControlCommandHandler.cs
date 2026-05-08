using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Game-agnostic handler for a registered control-plane command.</summary>
public interface IControlCommandHandler
{
    /// <summary>Metadata advertised to clients.</summary>
    ControlCommandDescriptor Descriptor { get; }

    /// <summary>Executes the command on the host-owned execution thread.</summary>
    ValueTask<ControlCommandResult> ExecuteAsync(
        ControlCommandContext context,
        JObject args,
        CancellationToken cancellationToken
    );
}
