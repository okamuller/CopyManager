using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CopyManager.Models;
using Microsoft.Extensions.Logging;

namespace CopyManager.Services;

public record FastCopyProgress(
    double ProgressPercent,
    double SpeedMbps,
    string? TimeRemaining,
    long TransferredBytes,
    long TotalBytes);

public interface IFastCopyService
{
    string BuildArguments(CopyJob job, AppSettings settings);
    Task<int> RunAsync(CopyJob job, AppSettings settings, IProgress<FastCopyProgress>? progress, CancellationToken cancellationToken);
    FastCopyProgress? ParseOutputLine(string line);
}

public class FastCopyService : IFastCopyService
{
    private readonly ILogger<FastCopyService> _logger;

    // Regex patterns per spec §7.2
    private static readonly Regex ProgressRegex = new(@"\((\d+\.?\d*)%\)", RegexOptions.Compiled);
    private static readonly Regex SpeedRegex = new(@"(\d+\.?\d*)\s*MB/s", RegexOptions.Compiled);
    private static readonly Regex EtaRegex = new(@"(?:ETA|Remaining)[:\s]+(\d+:\d+(?::\d+)?)", RegexOptions.Compiled);
    private static readonly Regex TransferredRegex = new(@"(\d[\d,]*)\s*/", RegexOptions.Compiled);
    private static readonly Regex TotalRegex = new(@"/\s*(\d[\d,]*)", RegexOptions.Compiled);

    public FastCopyService(ILogger<FastCopyService> logger)
    {
        _logger = logger;
    }

    public string BuildArguments(CopyJob job, AppSettings settings)
    {
        var cmd = job.Mode switch
        {
            CopyMode.Differential => "diff",
            CopyMode.ForceCopy => "force_copy",
            CopyMode.Move => "move",
            _ => "diff"
        };

        var bufferSize = job.Options.BufferSizeMb ?? settings.DefaultBufferSizeMb;
        var speed = job.Options.Speed ?? settings.DefaultSpeed;

        var args = $"/cmd={cmd}";
        args += $" /bufsize={bufferSize}m";
        args += $" /speed={speed}";
        args += " /log";

        if (!string.IsNullOrEmpty(job.LogFilePath))
            args += $" /logfile=\"{job.LogFilePath}\"";

        args += " /auto_close /no_confirm /error_stop";

        // Source paths
        foreach (var source in job.SourcePaths)
        {
            args += $" \"{source}\"";
        }

        // Destination (trailing backslash per FastCopy convention)
        var dest = job.DestinationPath;
        if (!dest.EndsWith('\\'))
            dest += "\\";
        args += $" /to=\"{dest}\"";

        return args;
    }

    public async Task<int> RunAsync(CopyJob job, AppSettings settings, IProgress<FastCopyProgress>? progress, CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(job, settings);
        _logger.LogInformation("Starting FastCopy: {Exe} {Args}", settings.FastCopyExePath, arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = settings.FastCopyExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Start();

        // Read stdout asynchronously for progress parsing
        var stdoutTask = ReadOutputAsync(process.StandardOutput, progress, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill FastCopy process");
            }
            throw;
        }

        var stderr = await stderrTask;
        await stdoutTask;

        if (!string.IsNullOrEmpty(stderr))
            _logger.LogWarning("FastCopy stderr: {Stderr}", stderr);

        _logger.LogInformation("FastCopy exited with code {ExitCode}", process.ExitCode);
        return process.ExitCode;
    }

    private async Task ReadOutputAsync(StreamReader reader, IProgress<FastCopyProgress>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            _logger.LogDebug("FastCopy output: {Line}", line);

            var parsed = ParseOutputLine(line);
            if (parsed is not null)
                progress?.Report(parsed);
        }
    }

    public FastCopyProgress? ParseOutputLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var progressMatch = ProgressRegex.Match(line);
        if (!progressMatch.Success)
            return null;

        var percent = double.Parse(progressMatch.Groups[1].Value);

        double speed = 0;
        var speedMatch = SpeedRegex.Match(line);
        if (speedMatch.Success)
            speed = double.Parse(speedMatch.Groups[1].Value);

        string? eta = null;
        var etaMatch = EtaRegex.Match(line);
        if (etaMatch.Success)
            eta = etaMatch.Groups[1].Value;

        long transferred = 0;
        var transferredMatch = TransferredRegex.Match(line);
        if (transferredMatch.Success)
            transferred = ParseLongWithCommas(transferredMatch.Groups[1].Value);

        long total = 0;
        var totalMatch = TotalRegex.Match(line);
        if (totalMatch.Success)
            total = ParseLongWithCommas(totalMatch.Groups[1].Value);

        return new FastCopyProgress(percent, speed, eta, transferred, total);
    }

    private static long ParseLongWithCommas(string value)
    {
        return long.TryParse(value.Replace(",", ""), out var result) ? result : 0;
    }
}
