using HotRepl.Protocol;
using Xunit;

namespace HotRepl.Tests.Unit;

public class SelectEvaluatorProtocolTests
{
    [Fact]
    public void RoundTrip_SelectEvaluatorMessage()
    {
        var msg = new SelectEvaluatorMessage { Id = "s-1", Evaluator = "Roslyn.Script" };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SelectEvaluatorMessage>(json);

        Assert.Equal(MessageType.SelectEvaluator, back.Type);
        Assert.Equal("s-1", back.Id);
        Assert.Equal("Roslyn.Script", back.Evaluator);
    }

    [Fact]
    public void RoundTrip_SelectEvaluatorResultMessage()
    {
        var msg = new SelectEvaluatorResultMessage
        {
            Id = "s-2",
            Success = true,
            Evaluator = "Mono.CSharp",
        };

        var back = MessageSerializer.Deserialize<SelectEvaluatorResultMessage>(
            MessageSerializer.Serialize(msg)
        );

        Assert.Equal(MessageType.SelectEvaluatorResult, back.Type);
        Assert.True(back.Success);
        Assert.Equal("Mono.CSharp", back.Evaluator);
    }

    [Fact]
    public void RoundTrip_SelectEvaluatorErrorMessage()
    {
        var msg = new SelectEvaluatorErrorMessage
        {
            Id = "s-3",
            ErrorKind = ErrorKind.Unsupported,
            Message = "Evaluator is not available.",
        };

        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SelectEvaluatorErrorMessage>(json);

        Assert.Equal(MessageType.SelectEvaluatorError, back.Type);
        Assert.Equal("unsupported", back.ErrorKind);
        Assert.Equal("Evaluator is not available.", back.Message);
    }
}
