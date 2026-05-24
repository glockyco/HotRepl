using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal dispatch shape consumed by the router. Typed handlers are
/// wrapped in <see cref="TypedCommandAdapter{TArgs, TOutput}"/> to
/// satisfy this interface. Not exposed publicly — consumers author
/// against <see cref="IControlCommandHandler{TArgs, TOutput}"/>.
/// </summary>
internal interface ICompiledControlCommand
{
    /// <summary>Descriptor advertised to clients via <c>command_describe</c>.</summary>
    ControlCommandDescriptor Descriptor { get; }

    /// <summary>Validate, deserialize, dispatch, and project the result.</summary>
    ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext context,
        JObject args,
        CancellationToken cancellationToken
    );
}
