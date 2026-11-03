using RecDog.Desktop;

internal static class StreamingRunnerTests
{
    public static async Task RunAsync()
    {
        var python = Path.GetFullPath(".tools/qwen-asr-env/Scripts/python.exe");
        var root = Path.Combine(Path.GetTempPath(), "RecDog-stream-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var worker = Path.Combine(root, "worker.py");
            await File.WriteAllTextAsync(worker, "import sys,json,base64\nprint(json.dumps({'type':'ready'}),flush=True)\nfor line in sys.stdin:\n m=json.loads(line)\n if m['type']=='audio': print(json.dumps({'type':'transcript','text':'实时文字','isFinal':False}),flush=True)\n if m['type']=='pause': print(json.dumps({'type':'transcript','text':'已确认句子','isFinal':True}),flush=True)\n if m['type']=='finish': break\n");
            var updates = new List<StreamingUpdate>();
            await using var runner = await StreamingTranscriptionRunner.StartAsync(python, root, worker, updates.Add, CancellationToken.None);
            runner.TrySendAudio(new byte[] { 1, 2, 3, 4 });
            await runner.PauseAsync(CancellationToken.None);
            await runner.ResumeAsync(CancellationToken.None);
            await runner.StopAsync(CancellationToken.None);
            if (!updates.Any(item => item.Text == "实时文字" && !item.IsFinal) || !updates.Any(item => item.Text == "已确认句子" && item.IsFinal)) throw new Exception("Streaming partial/final events missing");
            Console.WriteLine("PASS streaming worker ready, audio forwarding, partial/final, pause/resume and stop");
        }
        finally { Directory.Delete(root, true); }
    }
}
