using System.IO;
using Mono.CSharp;

namespace HotRepl.Evaluator.MonoCSharp;

internal sealed class ErrorOnlyStreamReportPrinter : StreamReportPrinter
{
    public ErrorOnlyStreamReportPrinter(TextWriter writer)
        : base(writer) { }

    public override bool HasRelatedSymbolSupport => true;

    public override void Print(AbstractMessage msg, bool showFullPath)
    {
        if (msg.IsWarning)
            return;

        base.Print(msg, showFullPath);
    }
}
