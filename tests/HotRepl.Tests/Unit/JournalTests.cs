using HotRepl.Journal;
using Xunit;

namespace HotRepl.Tests.Unit;

public class JournalTests
{
    [Fact]
    public void Journal_QueryReturnsEvalEntriesAfterReset()
    {
        var journal = new ReplJournal(capacity: 1024);
        journal.RecordEval("eval-1", success: true, durationMs: 7, errorKind: null);

        journal.RecordReset("reset-1");

        var entries = journal.Query(kind: "eval", limit: 20);
        var entry = Assert.Single(entries);
        Assert.Equal("eval-1", entry.Id);
        Assert.Equal("eval", entry.Kind);
        Assert.True(entry.Success);
        Assert.Equal(7, entry.DurationMs);
        Assert.Null(entry.ErrorKind);
    }

    [Fact]
    public void Journal_QueryCapsEntriesByLimitAndKind()
    {
        var journal = new ReplJournal(capacity: 2);
        journal.RecordEval("eval-1", success: true, durationMs: 1, errorKind: null);
        journal.RecordCommand("cmd-1", "game.quit", success: false, durationMs: 2, errorKind: "busy");
        journal.RecordEval("eval-2", success: false, durationMs: 3, errorKind: "timeout");

        var entries = journal.Query(kind: null, limit: 2);

        Assert.Collection(
            entries,
            entry => Assert.Equal("cmd-1", entry.Id),
            entry => Assert.Equal("eval-2", entry.Id)
        );
    }
}
