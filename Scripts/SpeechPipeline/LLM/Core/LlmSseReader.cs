using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Reads SSE data records incrementally. Returning false stops and releases the stream.</summary>
    internal static class LlmSseReader
    {
        private const int MaximumEventCharacters = 1024 * 1024;

        internal static async UniTask ReadAsync(Stream stream, Func<string, UniTask<bool>> onData, CancellationToken token)
        {
            using (var registration = token.Register(() => { try { stream.Dispose(); } catch { } }))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, false))
            {
                var buffer = new char[4096];
                var line = new StringBuilder();
                var data = new StringBuilder();
                var hasData = false;
                var skipLf = false;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int count;
                    try { count = await SpeechAsync.FromTask(reader.ReadAsync(buffer, 0, buffer.Length)); }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is DecoderFallbackException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw LlmErrorParser.Failure("stream_read_error", "LLM response stream could not be read.");
                    }
                    token.ThrowIfCancellationRequested();
                    if (count == 0) break; // SSE dispatch requires a blank line, even at EOF.
                    for (var i = 0; i < count; i++)
                    {
                        var character = buffer[i];
                        if (skipLf && character == '\n') { skipLf = false; continue; }
                        skipLf = false;
                        if (character != '\r' && character != '\n')
                        {
                            line.Append(character);
                            if (line.Length + data.Length > MaximumEventCharacters)
                                throw LlmErrorParser.Failure("event_too_large", "LLM stream event exceeded the size limit.");
                            continue;
                        }
                        skipLf = character == '\r';
                        if (line.Length == 0)
                        {
                            if (hasData)
                            {
                                token.ThrowIfCancellationRequested();
                                var value = data.ToString(0, data.Length - 1);
                                data.Clear();
                                hasData = false;
                                if (!await onData(value)) return;
                            }
                        }
                        else if (line.Length >= 5 && line.ToString(0, 5) == "data:")
                        {
                            var start = line.Length > 5 && line[5] == ' ' ? 6 : 5;
                            data.Append(line.ToString(start, line.Length - start)).Append('\n');
                            hasData = true;
                        }
                        else if (line.ToString() == "data") { data.Append('\n'); hasData = true; }
                        line.Clear();
                    }
                }
            }
        }
    }
}
