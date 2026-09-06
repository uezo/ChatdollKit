using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.Remote;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.Remote
{
    public class NativeAIAvatarConnectionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ConnectUsesOptionalBearerAuthAndSendsOneCompleteTextMessage(bool authenticate)
        {
            using (var server = new LoopbackServer())
            using (var connection = new NativeAIAvatarConnection())
            {
                var accepting = server.AcceptAsync();
                await connection.ConnectAsync(server.Uri, authenticate ? "test-only-token" : null, server.Token);
                using (var peer = await accepting)
                {
                    Assert.That(connection.IsOpen, Is.True);
                    if (authenticate) Assert.That(peer.Headers["Authorization"], Is.EqualTo("Bearer test-only-token"));
                    else Assert.That(peer.Headers.ContainsKey("Authorization"), Is.False);
                    var message = "{\"type\":\"data\",\"session_id\":\"remote\",\"audio_data\":\"AAEC\",\"text\":\"こんにちは::data:\"}";
                    await connection.SendAsync(message, server.Token);
                    var buffer = new byte[4096];
                    var received = await SpeechAsync.FromTask(peer.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), server.Token));
                    Assert.That(received.MessageType, Is.EqualTo(WebSocketMessageType.Text));
                    Assert.That(received.EndOfMessage, Is.True);
                    Assert.That(Encoding.UTF8.GetString(buffer, 0, received.Count), Is.EqualTo(message));
                    await SpeechAsync.FromTask(peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", server.Token));
                    Assert.That(await connection.ReceiveAsync(server.Token), Is.Null);
                    Assert.That(connection.IsOpen, Is.False);
                    Assert.DoesNotThrow(connection.Dispose);
                    Assert.DoesNotThrow(connection.Dispose);
                    Assert.DoesNotThrow(connection.Abort);
                }
            }
        }

        [Test]
        public async NUnitTask ReceivePreservesUtf8SplitAcrossFramesAndTheInternalReceiveBuffer()
        {
            using (var server = new LoopbackServer())
            using (var connection = new NativeAIAvatarConnection())
            {
                var accepting = server.AcceptAsync();
                await connection.ConnectAsync(server.Uri, null, server.Token);
                using (var peer = await accepting)
                {
                    var expected = "あ" + new string('x', 9000) + "終";
                    var bytes = Encoding.UTF8.GetBytes(expected);
                    var receiving = connection.ReceiveAsync(server.Token);
                    // The first frame ends in the middle of a three-byte character. The next exceeds 8192 bytes.
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(bytes, 0, 2), WebSocketMessageType.Text, false, server.Token));
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(bytes, 2, bytes.Length - 2), WebSocketMessageType.Text, true, server.Token));
                    Assert.That(await receiving, Is.EqualTo(expected));
                }
            }
        }

        [Test]
        public async NUnitTask ReceiveLimitCountsBytesAcrossFragmentsAndAcceptsTheExactLimit()
        {
            using (var server = new LoopbackServer())
            using (var connection = new NativeAIAvatarConnection(maximumMessageBytes: 5))
            {
                var accepting = server.AcceptAsync();
                await connection.ConnectAsync(server.Uri, null, server.Token);
                using (var peer = await accepting)
                {
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("12345")), WebSocketMessageType.Text, true, server.Token));
                    Assert.That(await connection.ReceiveAsync(server.Token), Is.EqualTo("12345"));
                    var bytes = Encoding.UTF8.GetBytes("ああ");
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(bytes, 0, 3), WebSocketMessageType.Text, false, server.Token));
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(bytes, 3, 3), WebSocketMessageType.Text, true, server.Token));
                    await SpeechAsyncAssert.ThrowsAsync<InvalidDataException>(async () => await connection.ReceiveAsync(server.Token));
                }
            }
        }

        [Test]
        public async NUnitTask BinaryMessagesAreRejectedInsteadOfBeingInterpretedAsJson()
        {
            using (var server = new LoopbackServer())
            using (var connection = new NativeAIAvatarConnection())
            {
                var accepting = server.AcceptAsync();
                await connection.ConnectAsync(server.Uri, null, server.Token);
                using (var peer = await accepting)
                {
                    await SpeechAsync.FromTask(peer.Socket.SendAsync(new ArraySegment<byte>(new byte[] { 123, 125 }), WebSocketMessageType.Binary, true, server.Token));
                    await SpeechAsyncAssert.ThrowsAsync<InvalidDataException>(async () => await connection.ReceiveAsync(server.Token));
                }
            }
        }

        [Test]
        public async NUnitTask CancellingAPendingReceiveSettlesTheWaitAndAbortsTheConnection()
        {
            using (var server = new LoopbackServer())
            using (var connection = new NativeAIAvatarConnection())
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(server.Token))
            {
                var accepting = server.AcceptAsync();
                await connection.ConnectAsync(server.Uri, null, server.Token);
                using (await accepting)
                {
                    var receiving = connection.ReceiveAsync(cancellation.Token);
                    Assert.That(receiving.Status, Is.EqualTo(UniTaskStatus.Pending));
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await receiving);
                    Assert.That(connection.IsOpen, Is.False);
                }
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonpositiveMessageLimitIsRejected(int maximum)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeAIAvatarConnection(maximum));
        }

        private sealed class LoopbackServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            private TcpClient client;
            public Uri Uri { get; }
            public CancellationToken Token => lifetime.Token;

            public LoopbackServer()
            {
                listener.Start();
                Uri = new Uri("ws://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/ws");
            }

            public async UniTask<Peer> AcceptAsync()
            {
                client = await SpeechAsync.FromTask(listener.AcceptTcpClientAsync());
                var stream = client.GetStream();
                var header = new StringBuilder();
                var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (header.Length >= 16384) throw new InvalidDataException("Loopback handshake was too large.");
                    if (await SpeechAsync.FromTask(stream.ReadAsync(one, 0, 1, Token)) == 0) throw new IOException("No loopback handshake.");
                    header.Append((char)one[0]);
                }
                var headers = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None).Skip(1)
                    .Where(line => line.Contains(":"))
                    .ToDictionary(line => line.Substring(0, line.IndexOf(':')),
                        line => line.Substring(line.IndexOf(':') + 1).Trim(), StringComparer.OrdinalIgnoreCase);
                string accept;
                using (var sha1 = SHA1.Create())
                    accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await SpeechAsync.FromTask(stream.WriteAsync(response, 0, response.Length, Token));
                return new Peer(WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(20)), headers);
            }

            public void Dispose()
            {
                lifetime.Cancel();
                listener.Stop();
                client?.Dispose();
                lifetime.Dispose();
            }
        }

        private sealed class Peer : IDisposable
        {
            public readonly WebSocket Socket;
            public readonly Dictionary<string, string> Headers;
            public Peer(WebSocket socket, Dictionary<string, string> headers) { Socket = socket; Headers = headers; }
            public void Dispose() => Socket.Dispose();
        }
    }
}
