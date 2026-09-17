using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace WolfEQ.Services;

public sealed class AutomationHost : IDisposable
{
    private static string UserKey => WindowsIdentity.GetCurrent().User!.Value;
    private readonly string PipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private bool _ownsMutex;
    private bool _disposed;

    public AutomationHost(string? instanceKey = null)
    {
        PipeName = $"WolfEQ-automation-{UserKey}-{instanceKey ?? System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString()}";
        _mutex = new Mutex(false, $"Local\\{PipeName}");
    }

    public bool TryOwn()
    {
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        return _ownsMutex;
    }

    public async Task<AutomationResult> SendAsync(string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        await WriteAsync(pipe, JsonSerializer.Serialize(args), timeout.Token);
        return JsonSerializer.Deserialize<AutomationResult>(await ReadAsync(pipe, timeout.Token))
            ?? new(1, "No response from WolfEQ.");
    }

    public async Task RunAsync(Func<string[], Task<AutomationResult>> execute)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                AutomationResult result;
                try
                {
                    var args = JsonSerializer.Deserialize<string[]>(await ReadAsync(pipe, deadline.Token)) ?? [];
                    result = await execute(args);
                }
                catch (Exception ex) { result = new(1, ex.Message); }
                await WriteAsync(pipe, JsonSerializer.Serialize(result), deadline.Token);
            }
            catch (OperationCanceledException) { if (_stop.IsCancellationRequested) break; }
            catch (IOException) { /* Disconnected caller; keep serving later requests. */ }
        }
    }

    private static async Task WriteAsync(Stream stream, string message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length > 65536) throw new IOException("Automation message exceeds 64 KiB.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task<string> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BitConverter.ToInt32(header);
        if (length is < 0 or > 65536) throw new IOException("Invalid automation message size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return Encoding.UTF8.GetString(bytes);
    }

    public static void Report(AutomationResult result)
    {
        Console.WriteLine(result.Message);
        try
        {
            AtomicFileWriter.WriteAllText(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "automation-result.json"),
                JsonSerializer.Serialize(result));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
