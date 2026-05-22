using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fleck;
using HotRepl.Server;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ClientEvictionTests
{
    [Fact]
    public void ClientRegistry_SendsSessionEvictedBeforeDisplacingActiveClient()
    {
        var events = new List<string>();
        var registry = new ClientRegistry(
            (socket, json) => events.Add($"{socket.ConnectionInfo.Id}:send:{json}"),
            _ => { }
        );
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new FakeSocket(firstId, events);
        var second = new FakeSocket(secondId, events);

        registry.OnConnected(firstId, first);
        registry.OnConnected(secondId, second);

        Assert.Collection(
            events,
            evt =>
            {
                Assert.StartsWith($"{firstId}:send:", evt, StringComparison.Ordinal);
                Assert.Contains("\"type\":\"session_evicted\"", evt, StringComparison.Ordinal);
                Assert.Contains("\"reason\":\"displaced\"", evt, StringComparison.Ordinal);
            },
            evt => Assert.Equal($"{firstId}:close", evt)
        );
    }

    private sealed class FakeSocket : IWebSocketConnection
    {
        private readonly List<string> _events;

        public FakeSocket(Guid id, List<string> events)
        {
            ConnectionInfo = new FakeConnectionInfo(id);
            _events = events;
        }

        public Action OnOpen { get; set; } = () => { };
        public Action OnClose { get; set; } = () => { };
        public Action<string> OnMessage { get; set; } = _ => { };
        public Action<byte[]> OnBinary { get; set; } = _ => { };
        public Action<byte[]> OnPing { get; set; } = _ => { };
        public Action<byte[]> OnPong { get; set; } = _ => { };
        public Action<Exception> OnError { get; set; } = _ => { };
        public IWebSocketConnectionInfo ConnectionInfo { get; }
        public bool IsAvailable { get; set; } = true;

        public Task Send(string message) => Task.CompletedTask;
        public Task Send(byte[] message) => Task.CompletedTask;
        public Task SendPing(byte[] message) => Task.CompletedTask;
        public Task SendPong(byte[] message) => Task.CompletedTask;

        public void Close()
        {
            IsAvailable = false;
            _events.Add($"{ConnectionInfo.Id}:close");
        }

        public void Close(int code) => Close();
    }

    private sealed class FakeConnectionInfo : IWebSocketConnectionInfo
    {
        public FakeConnectionInfo(Guid id) => Id = id;

        public string SubProtocol => "";
        public string Origin => "";
        public string Host => "localhost";
        public string Path => "/";
        public string ClientIpAddress => "127.0.0.1";
        public int ClientPort => 12345;
        public IDictionary<string, string> Cookies { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public IDictionary<string, string> Headers { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public Guid Id { get; }
        public string NegotiatedSubProtocol => "";
    }
}
