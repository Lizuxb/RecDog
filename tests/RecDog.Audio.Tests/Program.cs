using RecDog.Desktop;

var failures = 0;
await Test("Changed device name is rejected before capture", async () => {
    AudioRecorder.RequireDeviceName("USB麦克风", "USB麦克风");
    await Reject(() => { AudioRecorder.RequireDeviceName("USB麦克风", "内置麦克风"); return Task.CompletedTask; });
});
await Test("Recovery WAV cleanup failure cannot hide a completed MP3", () => {
    var dir = Path.Combine(Path.GetTempPath(), "RecDog-cleanup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try {
        var wav = Path.Combine(dir, "recovery.wav"); Directory.CreateDirectory(wav);
        var mp3 = Path.Combine(dir, "录音.mp3"); File.WriteAllText(mp3, "encoded");
        var result = AudioRecorder.CompleteArchive(wav, mp3, 2000);
        if (result.FilePath != mp3 || result.DurationMs != 2000 || !File.Exists(mp3)) throw new Exception("Completed MP3 lost");
    } finally { Directory.Delete(dir, true); }
    return Task.CompletedTask;
});
await Test("PCM peak measures silence, loud input and negative full scale", () => {
    if (AudioRecorder.MeasurePeak(new byte[8], 8) != 0) throw new Exception("Silence");
    var pcm = new byte[] { 0, 0, 0, 64, 0, 128 };
    if (AudioRecorder.MeasurePeak(pcm, pcm.Length) != 1) throw new Exception("Full scale");
    return Task.CompletedTask;
});
await Test("Readable recording paths are safe and avoid collisions", () => {
    var dir = Path.Combine(Path.GetTempPath(), "RecDog-name-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try {
        var path = AudioRecorder.AvailablePath(dir, "人员_访谈:记录_20261103_120000"); File.WriteAllText(path, "fixture");
        var next = AudioRecorder.AvailablePath(dir, "人员_访谈:记录_20261103_120000");
        if (path == next || !Path.GetFileName(next).Contains("人员_访谈_记录") || !next.EndsWith("_2.mp3")) throw new Exception("Unsafe or duplicate filename");
    } finally { Directory.Delete(dir, true); }
    return Task.CompletedTask;
});
await Test("Pause without recording is rejected", async () => { var recorder = new AudioRecorder(); await Reject(() => { recorder.Pause(); return Task.CompletedTask; }); });
await Test("Resume without recording is rejected", async () => { var recorder = new AudioRecorder(); await Reject(() => { recorder.Resume(); return Task.CompletedTask; }); });
await Test("Stop without recording is rejected", async () => { var recorder = new AudioRecorder(); await Reject(async () => await recorder.StopAsync()); });
await Test("Dispose is idempotent and prevents starting", async () => { var recorder = new AudioRecorder(); recorder.Dispose(); recorder.Dispose(); await Reject(async () => await recorder.StartAsync(Path.GetTempPath())); });
await Test("Synthetic PCM archives and decodes as MP3", async () => {
    var directory = Path.Combine(Path.GetTempPath(), "RecDog-audio-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
        var wav = Path.Combine(directory, "source.wav"); var mp3 = Path.Combine(directory, "output.mp3");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(44100, 16, 1))) {
            for (var i = 0; i < 88200; i++) { var value = (short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 2000); writer.Write(BitConverter.GetBytes(value), 0, 2); }
        }
        var method = typeof(AudioRecorder).GetMethod("EncodeMp3", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(null, new object[] { wav, mp3 });
        if (!File.Exists(mp3) || new FileInfo(mp3).Length < 1000) throw new Exception("MP3 missing");
        using var reader = new NAudio.Wave.Mp3FileReader(mp3);
        if (reader.TotalTime.TotalSeconds < 1.9) throw new Exception("MP3 duration wrong");
        var fixtureIndex = Array.IndexOf(args, "--fixture");
        if (fixtureIndex >= 0 && fixtureIndex + 1 < args.Length) {
            var repository = new RecDog.Core.WorkspaceRepository(args[fixtureIndex + 1]);
            await repository.LoadOrCreateAsync();
            await repository.CreateFolderAsync("media-person", "音频验证人员");
            var relative = "recordings/media-person/fixture.mp3";
            var target = repository.ResolveRecordingPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(mp3, target);
            await repository.SaveRecordingAsync("media-person", new RecDog.Core.WorkspaceRecording("fixture", "合成音频验收", relative, 2000, DateTimeOffset.UtcNow));
        }
        await Task.CompletedTask;
    } finally { Directory.Delete(directory, true); }
});
Console.WriteLine($"Audio tests: {9 - failures}/9 passed");
return failures == 0 ? 0 : 1;

async Task Test(string name, Func<Task> run) { try { await run(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.ToString()); } }
static async Task Reject(Func<Task> run) { try { await run(); } catch (InvalidOperationException) { return; } throw new Exception("Expected inactive-state rejection"); }
