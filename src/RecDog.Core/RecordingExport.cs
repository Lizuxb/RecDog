namespace RecDog.Core;

public static class RecordingExport
{
    /// <summary>Copies MP3 frames without leading ID3 metadata that some Windows players reject.</summary>
    public static async Task CopyForPlaybackAsync(string sourcePath, string destinationPath)
    {
        await using var source = File.OpenRead(sourcePath);
        var header = new byte[10];
        var read = (int)Math.Min(header.Length, source.Length);
        await source.ReadExactlyAsync(header.AsMemory(0, read));
        source.Position = 0;
        if (read >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
        {
            if (read != 10 || header[3] is < 2 or > 4 || header.Skip(6).Any(b => b > 127))
                throw new InvalidDataException("录音的 MP3 标签无效，原录音未修改。");
            long offset = 10L + (header[6] << 21) + (header[7] << 14) + (header[8] << 7) + header[9];
            if (header[3] == 4 && (header[5] & 0x10) != 0) offset += 10; // ID3v2.4 footer
            if (offset + 4 > source.Length) throw new InvalidDataException("录音文件的 MP3 标签不完整，原录音未修改。");
            source.Position = offset;
            var frame = new byte[4]; await source.ReadExactlyAsync(frame);
            if (frame[0] != 0xff || (frame[1] & 0xe0) != 0xe0)
                throw new InvalidDataException("录音标签后的 MP3 音频头无效，原录音未修改。");
            source.Position = offset;
        }
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        await source.CopyToAsync(output);
    }
}
