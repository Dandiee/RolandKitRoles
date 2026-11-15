using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace KitRoles.Midi;

/// <summary>SysEx access to the V51 over USB MIDI: Identity Request, RQ1 (read) and DT1 (write).</summary>
public sealed class V51Client : IDisposable
{
    private static readonly byte[] Model = [0x01, 0x06, 0x01];
    private const byte DeviceId = 0x10;

    private readonly InputDevice _in;
    private readonly OutputDevice _out;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<byte[]>? _pending;
    private Func<byte[], bool>? _match;

    private V51Client(InputDevice input, OutputDevice output)
    {
        _in = input;
        _out = output;
        _in.EventReceived += OnEvent;
        _in.StartEventsListening();
    }

    public static V51Client? TryOpen(string name = "V51")
    {
        var input = InputDevice.GetAll().FirstOrDefault(d => d.Name.Contains(name));
        var output = OutputDevice.GetAll().FirstOrDefault(d => d.Name.Contains(name));
        if (input is null || output is null)
        {
            input?.Dispose();
            output?.Dispose();
            return null;
        }
        return new V51Client(input, output);
    }

    /// <summary>Returns the firmware version string from the Identity Reply, or null.</summary>
    public async Task<string?> IdentifyAsync(CancellationToken ct = default)
    {
        // F0 7E 7F 06 01 F7 -> F0 7E dev 06 02 41 01 06 02 00 r1 r2 r3 r4 F7
        var reply = await RequestAsync([0x7E, 0x7F, 0x06, 0x01, 0xF7],
            d => d.Length >= 14 && d[0] == 0x7E && d[2] == 0x06 && d[3] == 0x02, ct);
        return reply is null ? null : $"{reply[10]}.{reply[11]}{reply[12]}";
    }

    /// <summary>Reads one block (RQ1 -> DT1). Returns the payload, or null on timeout.</summary>
    public async Task<byte[]?> ReadAsync(int address, int size, CancellationToken ct = default)
    {
        var body = KitMap.To7(address).Concat(KitMap.To7(size)).ToArray();
        byte[] msg = [0x41, DeviceId, .. Model, 0x11, .. body, Checksum(body), 0xF7];
        var addr = KitMap.To7(address);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var reply = await RequestAsync(msg, d => IsDt1(d) && d.AsSpan(6, 4).SequenceEqual(addr), ct);
            if (reply is null) continue;
            var payload = reply[10..^2];
            if (payload.Length == size && Checksum(reply[6..^2]) == reply[^2]) return payload;
        }
        return null;
    }

    /// <summary>Writes one block (DT1, max 256 bytes) and waits the required gap before the next packet.</summary>
    public async Task WriteAsync(int address, byte[] data, CancellationToken ct = default)
    {
        if (data.Length > 256) throw new ArgumentException("DT1 packets are limited to 256 bytes.");
        var body = KitMap.To7(address).Concat(data).ToArray();
        await _gate.WaitAsync(ct);
        try
        {
            _out.SendEvent(new NormalSysExEvent([0x41, DeviceId, .. Model, 0x12, .. body, Checksum(body), 0xF7]));
            await Task.Delay(50, ct); // manual: >= 20 ms between packets
        }
        finally { _gate.Release(); }
    }

    private static bool IsDt1(byte[] d) =>
        d.Length > 12 && d[0] == 0x41 && d.AsSpan(2, 3).SequenceEqual(Model) && d[5] == 0x12;

    private async Task<byte[]?> RequestAsync(byte[] sysexWithoutF0, Func<byte[], bool> match, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _match = match;
            _pending = tcs;
            _out.SendEvent(new NormalSysExEvent(sysexWithoutF0));
            var done = await Task.WhenAny(tcs.Task, Task.Delay(1000, ct));
            ct.ThrowIfCancellationRequested();
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            _pending = null;
            _gate.Release();
        }
    }

    private void OnEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        if (e.Event is SysExEvent sx && _pending is { } tcs && _match?.Invoke(sx.Data) == true)
            tcs.TrySetResult(sx.Data);
    }

    private static byte Checksum(ReadOnlySpan<byte> body)
    {
        int sum = 0;
        foreach (var b in body) sum += b;
        return (byte)((128 - sum % 128) % 128);
    }

    public void Dispose()
    {
        _in.EventReceived -= OnEvent;
        _in.Dispose();
        _out.Dispose();
    }
}
