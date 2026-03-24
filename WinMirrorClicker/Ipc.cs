using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace WinMirrorClicker;

internal sealed record MirrorClickCommand(int ClientX, int ClientY, int DelayMs, int SourceClientW = 0, int SourceClientH = 0);

internal static class IpcDefaults
{
    internal const string PipeName = "WinMirrorClicker.MirrorPipe";
}

internal sealed class TargetAgentServer : IDisposable
{
    private readonly string _pipeName;

    internal event Action<MirrorClickCommand>? CommandReceived;

    internal TargetAgentServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = CreateServer(_pipeName);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ReadLoopAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task ReadLoopAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        while (!cancellationToken.IsCancellationRequested && server.IsConnected)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return;

            MirrorClickCommand? cmd;
            try
            {
                cmd = JsonSerializer.Deserialize<MirrorClickCommand>(line);
            }
            catch
            {
                continue;
            }

            if (cmd is null) continue;
            CommandReceived?.Invoke(cmd);
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

internal sealed class SourceAgentClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _client;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    internal bool IsConnected => _client?.IsConnected == true;

    internal SourceAgentClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    internal async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected) return true;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected) return true;

            _writer?.Dispose();
            _client?.Dispose();

            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(300);
                await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                return false;
            }

            _client = client;
            _writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            return true;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    internal async Task<bool> SendAsync(MirrorClickCommand command, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false)) return false;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected) return false;
            var json = JsonSerializer.Serialize(command);
            await _writer!.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            _writer?.Dispose();
            _client?.Dispose();
            _writer = null;
            _client = null;
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _client?.Dispose();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
