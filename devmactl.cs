#!/usr/bin/env dotnet-script
#:sdk Microsoft.NET.Sdk.Web
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");

var AllowedCommands = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
{
    ["colima"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "list",
        "start",
        "stop"
    },
    ["docker"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version",
        "version",
        "context show",
        "info --format {{.NCPU}}",
        "info --format {{.MemTotal}}"
    },
    ["osascript"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "-e tell application \"Docker\" to quit"
    },
    ["node"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version"
    },
    ["npm"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version"
    },
    ["dotnet"] = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version",
        "sdk check",
        "--list-runtimes"
    }
};

var app = builder.Build();

var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        ctx.Response.Headers.Pragma = "no-cache";
        ctx.Response.Headers.Expires = "0";
    }

    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(wwwroot),
    RequestPath = ""
});

app.MapGet("/", async ctx =>
{
    ctx.Response.ContentType = "text/html";
    await ctx.Response.SendFileAsync(Path.Combine(wwwroot, "index.html"));
});

app.MapGet("/api/status", async ctx =>
{
    var colima = await GetColimaJson();
    var docker = await GetDockerJson();
    var node   = await GetNodeJson();
    var dotnet = await GetDotnetJson();
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync($"{{\"colima\":{colima},\"docker\":{docker},\"node\":{node},\"dotnet\":{dotnet}}}");
});

app.MapGet("/api/status/colima",  async ctx => { ctx.Response.ContentType = "application/json"; await ctx.Response.WriteAsync(await GetColimaJson()); });
app.MapGet("/api/status/docker",  async ctx => { ctx.Response.ContentType = "application/json"; await ctx.Response.WriteAsync(await GetDockerJson()); });
app.MapGet("/api/status/node",    async ctx => { ctx.Response.ContentType = "application/json"; await ctx.Response.WriteAsync(await GetNodeJson()); });
app.MapGet("/api/status/dotnet",  async ctx => { ctx.Response.ContentType = "application/json"; await ctx.Response.WriteAsync(await GetDotnetJson()); });

app.MapPost("/api/colima/start", async ctx =>
{
    var r = await RunCommand("colima", "start");
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync($"{{\"ok\":{(r.success ? "true" : "false")},\"output\":{JsonString(r.output)}}}");
});

app.MapPost("/api/colima/stop", async ctx =>
{
    var r = await RunCommand("colima", "stop");
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync($"{{\"ok\":{(r.success ? "true" : "false")},\"output\":{JsonString(r.output)}}}");
});

app.MapPost("/api/docker/stop", async ctx =>
{
    var r = await StopDocker();
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync($"{{\"ok\":{(r.success ? "true" : "false")},\"output\":{JsonString(r.output)}}}");
});

Console.WriteLine("devmachinectl running → http://localhost:5050");
await app.RunAsync();

// ── helpers ──────────────────────────────────────────────────────────────────

async Task<string> GetColimaJson()
{
    var r = await RunCommand("colima", "status");
    if (!r.success && IsNotFound(r.output))
        return "{\"installed\":false}";

    bool running = r.success && r.output.Contains("running", StringComparison.OrdinalIgnoreCase);
    var list = await RunCommand("colima", "list");
    return $"{{\"installed\":true,\"running\":{(running ? "true" : "false")},\"raw\":{JsonString(r.output.Trim())},\"list\":{JsonString(list.output.Trim())}}}";
}

async Task<string> GetDockerJson()
{
    var ver = await RunCommand("docker", "--version");
    if (!ver.success && IsNotFound(ver.output))
        return "{\"installed\":false}";

    var cpuR = await RunCommand("docker", "info --format {{.NCPU}}");
    bool running = cpuR.success;
    string version = ver.output.Trim();
    string cpus = "", memory = "", context = "";

    if (running)
    {
        var memR = await RunCommand("docker", "info --format {{.MemTotal}}");
        if (cpuR.success) cpus = cpuR.output.Trim();
        if (memR.success && long.TryParse(memR.output.Trim(), out var bytes))
            memory = FormatBytes(bytes);
    }

    var contextR = await RunCommand("docker", "context show");
    if (contextR.success) context = contextR.output.Trim();

    return $"{{\"installed\":true,\"running\":{(running ? "true" : "false")},\"version\":{JsonString(version)},\"context\":{JsonString(context)},\"cpus\":{JsonString(cpus)},\"memory\":{JsonString(memory)}}}";
}

async Task<(bool success, string output)> StopDocker()
{
    // If Docker is backed by Colima, stopping Colima also stops the Docker daemon.
    var contextR = await RunCommand("docker", "context show");
    if (contextR.success && contextR.output.Contains("colima", StringComparison.OrdinalIgnoreCase))
    {
        var colimaStop = await RunCommand("colima", "stop");
        if (colimaStop.success)
            return (true, "Docker context is colima. Colima has been stopped.");
    }

    // On macOS with Docker Desktop, quit the app to stop the daemon.
    var quitDesktop = await RunCommand("osascript", "-e tell application \"Docker\" to quit");
    if (quitDesktop.success)
        return (true, "Docker Desktop quit signal sent.");

    return (false, contextR.success
        ? $"Unable to stop Docker. Context: {contextR.output.Trim()}"
        : "Unable to determine Docker context or stop Docker.");
}

async Task<string> GetNodeJson()
{
    var ver = await RunCommand("node", "--version");
    if (!ver.success)
        return "{\"installed\":false}";
    var npm = await RunCommand("npm", "--version");
    return $"{{\"installed\":true,\"version\":{JsonString(ver.output.Trim())},\"npm\":{JsonString(npm.output.Trim())}}}";
}

async Task<string> GetDotnetJson()
{
    var ver = await RunCommand("dotnet", "--version");
    if (!ver.success)
        return "{\"installed\":false}";
    var sdks = await RunCommand("dotnet", "sdk check");
    var runtimes = await RunCommand("dotnet", "--list-runtimes");
    return $"{{\"installed\":true,\"version\":{JsonString(ver.output.Trim())},\"sdkCheck\":{JsonString(sdks.output.Trim())},\"runtimes\":{JsonString(runtimes.output.Trim())}}}";
}

async Task<(bool success, string output)> RunCommand(string cmd, string args)
{
    if (!IsCommandAllowed(cmd, args))
    {
        return (false, $"Command not allowed: {cmd} {args}");
    }

    try
    {
        var psi = new ProcessStartInfo(cmd, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode == 0, string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
    }
    catch (Exception ex)
    {
        return (false, ex.Message);
    }
}

bool IsCommandAllowed(string cmd, string args)
{
    if (string.IsNullOrWhiteSpace(cmd) || cmd.Contains(' '))
        return false;

    if (!AllowedCommands.TryGetValue(cmd, out var allowedArgs))
        return false;

    var normalizedArgs = NormalizeCommandArgs(args);
    if (string.IsNullOrWhiteSpace(normalizedArgs))
        return false;

    return allowedArgs.Contains(normalizedArgs);
}

string NormalizeCommandArgs(string args)
{
    if (string.IsNullOrWhiteSpace(args))
        return string.Empty;

    var normalized = string.Join(" ",
        args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    return normalized.Trim();
}

bool IsNotFound(string msg) =>
    msg.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
    msg.Contains("No such file", StringComparison.OrdinalIgnoreCase);

string JsonString(string? s)
{
    if (s is null) return "null";
    var sb = new StringBuilder(s.Length + 2);
    sb.Append('"');
    foreach (char c in s)
    {
        switch (c)
        {
            case '"':  sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n");  break;
            case '\r': sb.Append("\\r");  break;
            case '\t': sb.Append("\\t");  break;
            default:
                if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                else sb.Append(c);
                break;
        }
    }
    sb.Append('"');
    return sb.ToString();
}

string FormatBytes(long bytes)
{
    if (bytes <= 0) return "0 B";
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    int i = (int)Math.Floor(Math.Log(bytes, 1024));
    i = Math.Min(i, units.Length - 1);
    return $"{bytes / Math.Pow(1024, i):0.##} {units[i]}";
}

