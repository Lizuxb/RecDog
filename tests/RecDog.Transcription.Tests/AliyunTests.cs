using System.Text.Json;
using RecDog.Desktop;

internal static class AliyunTests
{
    public static void Run()
    {
        using var start = JsonDocument.Parse(AliyunProtocol.Start("app", "0123456789abcdef0123456789abcdef"));
        var payload = start.RootElement.GetProperty("payload");
        if (payload.GetProperty("sample_rate").GetInt32() != 16000 || !payload.GetProperty("enable_punctuation_prediction").GetBoolean()) throw new Exception("Invalid start payload");
        if (AliyunProtocol.Parse("{\"header\":{\"name\":\"SentenceEnd\",\"status\":20000000},\"payload\":{\"index\":2,\"result\":\"你好。\"}}") is not { Text: "你好。", IsFinal: true, SentenceId: 2 }) throw new Exception("Final mapping failed");
        if (AliyunProtocol.Parse("{\"header\":{\"name\":\"TaskFailed\",\"status\":40000001,\"status_text\":\"Invalid token\"}}")?.Error is null) throw new Exception("Failure lost");
        var resampler = new StreamingPcmResampler();
        var bytes = Enumerable.Range(0, 10).Sum(_ => resampler.Convert(new byte[8820]).Length);
        if (bytes < 31000 || bytes > 32200) throw new Exception("Resample duration changed: " + bytes);
        Console.WriteLine("PASS Aliyun start parameters, final/error mapping and continuous PCM conversion");
    }
}
