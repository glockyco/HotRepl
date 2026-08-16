using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HotRepl.Evaluator.MonoCSharp;
using Mono.CSharp;
using Xunit;

namespace HotRepl.Tests.Unit;

public class MonoCSharpEvaluatorTests
{
    [Fact]
    public void ReportPrinter_UsesCanonicalMonoDiagnosticFormat()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var printer = new ErrorOnlyStreamReportPrinter(output);
        var location = CreateLocation("snippet.cs", "/source/snippet.cs", 1, 2);

        printer.Print(
            new TestMessage(
                isWarning: false,
                code: 127,
                location,
                "A return keyword must not be followed by any expression when method returns void"
            ),
            showFullPath: false
        );

        Assert.Equal(
            $"snippet.cs(1,2): error CS0127: A return keyword must not be followed by any expression when method returns void{Environment.NewLine}",
            output.ToString()
        );
        Assert.Equal(1, printer.ErrorsCount);
    }

    [Fact]
    public void ReportPrinter_PreservesDiagnosticOrderAndRelatedSymbols()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var printer = new ErrorOnlyStreamReportPrinter(output);
        var location = CreateLocation("snippet.cs", "/source/snippet.cs", 3, 4);

        Assert.True(printer.HasRelatedSymbolSupport);
        printer.Print(
            new TestMessage(isWarning: false, code: 1002, location, "; expected"),
            showFullPath: false
        );
        printer.Print(
            new TestMessage(
                isWarning: false,
                code: 121,
                location,
                "The call is ambiguous",
                new[]
                {
                    "snippet.cs(1,1): (Location of the symbol related to previous ",
                    "snippet.cs(2,1): (Location of the symbol related to previous ",
                }
            ),
            showFullPath: false
        );

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "snippet.cs(3,4): error CS1002: ; expected",
                "snippet.cs(3,4): error CS0121: The call is ambiguous",
                "snippet.cs(1,1): (Location of the symbol related to previous error)",
                "snippet.cs(2,1): (Location of the symbol related to previous error)",
                ""
            ),
            output.ToString()
        );
        Assert.Equal(2, printer.ErrorsCount);
    }

    [Fact]
    public void ReportPrinter_SuppressesWarningsWithoutChangingCounts()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var printer = new ErrorOnlyStreamReportPrinter(output);
        var location = CreateLocation("snippet.cs", "/source/snippet.cs", 1, 1);

        printer.Print(
            new TestMessage(isWarning: true, code: 1030, location, "#warning: expected"),
            showFullPath: false
        );

        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(0, printer.ErrorsCount);
        Assert.Equal(0, printer.WarningsCount);
    }

    private static Location CreateLocation(string name, string fullPath, int row, int column)
    {
        Location.Reset();
        var source = new SourceFile(name, fullPath, index: 1);
        Location.Initialize(new List<SourceFile> { source });
        return new Location(source, row, column);
    }

    private sealed class TestMessage : AbstractMessage
    {
        private readonly bool _isWarning;

        public TestMessage(
            bool isWarning,
            int code,
            Location location,
            string message,
            IReadOnlyCollection<string>? relatedSymbols = null
        )
            : base(
                code,
                location,
                message,
                relatedSymbols == null ? new List<string>() : new List<string>(relatedSymbols)
            )
        {
            _isWarning = isWarning;
        }

        public override bool IsWarning => _isWarning;
        public override string MessageType => _isWarning ? "warning" : "error";
    }
}
