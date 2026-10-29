using System.Diagnostics;
using System.Net;
using System.Text.Json;

sealed class NodeHttpHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath("tests/http-bridge.mjs"));
        using var process = Process.Start(start) ?? throw new Exception("Node test transport did not start");
        using var registration = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var input = JsonSerializer.Serialize(new { url = request.RequestUri!.AbsoluteUri, method = request.Method.Method, headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct) });
        if (request.Content is not null)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(input)!;
            node["headers"]!["Content-Type"] = request.Content.Headers.ContentType?.ToString() ?? "application/json";
            input = node.ToJsonString();
        }
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct); var errors = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new HttpRequestException("Node test transport: " + await errors);
        using var json = JsonDocument.Parse(await output); var root = json.RootElement;
        var response = new HttpResponseMessage((HttpStatusCode)root.GetProperty("status").GetInt32()) {
            RequestMessage = new HttpRequestMessage(request.Method, root.GetProperty("url").GetString()),
            Content = new ByteArrayContent(Convert.FromBase64String(root.GetProperty("data").GetString()!)) };
        foreach (var header in root.GetProperty("headers").EnumerateObject())
            if (header.Name is not "content-length" and not "content-encoding" and not "transfer-encoding")
                if (!response.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString())) response.Content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
        return response;
    }
}
