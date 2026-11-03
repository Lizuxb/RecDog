using System.Buffers.Binary;
using NAudio.Dsp;

namespace RecDog.Desktop;

/// <summary>Continuous filtered 44.1kHz mono PCM16 to 16kHz PCM16, preserving phase between blocks.</summary>
public sealed class StreamingPcmResampler
{
    private readonly WdlResampler resampler = new();
    public StreamingPcmResampler()
    {
        resampler.SetMode(true, 2, false);
        resampler.SetFeedMode(true);
        resampler.SetRates(44100, 16000);
    }
    public byte[] Convert(byte[] input)
    {
        var count = input.Length / 2;
        var prepared = resampler.ResamplePrepare(count, 1, out var buffer, out var offset);
        if (prepared != count) throw new InvalidOperationException("音频重采样缓冲区不足。");
        for (var i = 0; i < count; i++) buffer[offset + i] = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(i * 2, 2)) / 32768f;
        var output = new float[(int)Math.Ceiling(count * 16000d / 44100) + 64];
        var frames = resampler.ResampleOut(output, 0, count, output.Length, 1);
        var result = new byte[frames * 2];
        for (var i = 0; i < frames; i++) BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(i * 2), (short)Math.Clamp((int)Math.Round(output[i] * 32768), -32768, 32767));
        return result;
    }
}
