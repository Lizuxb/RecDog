using RecDog.Desktop;
using NAudio.Wave;

string? Argument(string name) { var index = Array.IndexOf(args, name); return index < 0 ? null : args[index + 1]; }
AliyunTests.Run();
if (Argument("--real-worker") is { } realWorker)
{
    var temporary = Path.Combine(Path.GetTempPath(), "RecDog-real-runner-" + Guid.NewGuid().ToString("N") + ".wav");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        TranscriptionRunner.PrepareAudio(Argument("--audio")!, temporary, CancellationToken.None);
        using (var prepared = new WaveFileReader(temporary))
            if (prepared.WaveFormat.SampleRate != 16000 || prepared.WaveFormat.Channels != 1 || prepared.WaveFormat.BitsPerSample != 16) throw new Exception("Invalid prepared WAV format");
        var result = await TranscriptionRunner.RunWorkerAsync(Argument("--python")!, Argument("--model")!, realWorker, temporary,
            update => Console.WriteLine($"{update.Type} {update.CompletedSeconds}/{update.TotalSeconds}"), CancellationToken.None, TimeSpan.FromMinutes(10));
        if (!result.Text.Any(c => c >= '\u4e00' && c <= '\u9fff')) throw new Exception("Expected a Chinese transcription");
        Console.WriteLine($"PASS REAL elapsed={watch.Elapsed.TotalSeconds:F2}s language={result.Language} text={result.Text}");
    }
    finally { File.Delete(temporary); }
    return;
}

var python = Path.GetFullPath(".tools/qwen-asr-env/Scripts/python.exe");
var root = Path.Combine(Path.GetTempPath(), "RecDog-runner-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "source.wav"); var prepared = Path.Combine(root, "prepared.wav");
    using (var wav = new WaveFileWriter(source, new WaveFormat(48000, 16, 2))) wav.Write(new byte[48000 * 4], 0, 48000 * 4);
    TranscriptionRunner.PrepareAudio(source, prepared, CancellationToken.None);
    using (var wav = new WaveFileReader(prepared))
    {
        Equal(16000, wav.WaveFormat.SampleRate); Equal(1, wav.WaveFormat.Channels); Equal(16, wav.WaveFormat.BitsPerSample);
        if (Math.Abs(wav.TotalTime.TotalSeconds - 1) > .01) throw new Exception("Prepared audio duration changed");
    }
    var worker = Path.Combine(root, "worker.py");
    await File.WriteAllTextAsync(worker, "import sys,json\nx=json.loads(sys.stdin.readline())\nassert x['audioPath'].endswith('.wav')\nprint(json.dumps({'type':'progress','completedSeconds':2,'totalSeconds':3}),flush=True)\nprint(json.dumps({'type':'completed','text':'中文','language':'Chinese'}),flush=True)\n");
    var updates = new List<WorkerUpdate>();
    var result = await TranscriptionRunner.RunWorkerAsync(python, root, worker, Path.Combine(root,"input.wav"), updates.Add, CancellationToken.None, TimeSpan.FromSeconds(10));
    Equal("中文", result.Text); Equal(2d, updates.Single().CompletedSeconds);
    await File.WriteAllTextAsync(worker, "import time,sys\nsys.stdin.readline()\nsys.stderr.write('x'*100000);sys.stderr.flush()\ntime.sleep(60)\n");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try { await TranscriptionRunner.RunWorkerAsync(python, root, worker, "input.wav", _ => {}, cancellation.Token, TimeSpan.FromSeconds(10)); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
    if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Cancellation was not bounded");
    await File.WriteAllTextAsync(worker, "import sys\nsys.stdin.readline()\nprint('{\"type\":\"progress\",\"completedSeconds\":-1,\"totalSeconds\":2}',flush=True)\n");
    try { await TranscriptionRunner.RunWorkerAsync(python, root, worker, "input.wav", _ => {}, CancellationToken.None, TimeSpan.FromSeconds(10)); throw new Exception("Invalid protocol accepted"); }
    catch (InvalidDataException) { }
    await File.WriteAllTextAsync(worker, "import time,sys\nsys.stdin.readline()\ntime.sleep(60)\n");
    try { await TranscriptionRunner.RunWorkerAsync(python, root, worker, "input.wav", _ => {}, CancellationToken.None, TimeSpan.FromMilliseconds(250)); throw new Exception("Timeout ignored"); }
    catch (TimeoutException) { }
    await File.WriteAllTextAsync(worker, "import sys\nsys.stdin.readline()\nsys.exit(7)\n");
    try { await TranscriptionRunner.RunWorkerAsync(python, root, worker, "input.wav", _ => {}, CancellationToken.None, TimeSpan.FromSeconds(10)); throw new Exception("Process failure ignored"); }
    catch (InvalidOperationException) { }
    Console.WriteLine("PASS PCM conversion, worker completion, bounded stderr/cancellation, invalid progress, timeout and process failure");
await StreamingRunnerTests.RunAsync();
}
finally { Directory.Delete(root, true); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
