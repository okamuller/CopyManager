using CopyManager.Models;
using CopyManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CopyManager.Tests;

public class FastCopyServiceTests
{
    private readonly FastCopyService _service;

    public FastCopyServiceTests()
    {
        var logger = new Mock<ILogger<FastCopyService>>();
        _service = new FastCopyService(logger.Object);
    }

    #region BuildArguments Tests

    [Fact]
    public void BuildArguments_Differential_ContainsDiffCommand()
    {
        var job = CreateTestJob(CopyMode.Differential);
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/cmd=diff", args);
    }

    [Fact]
    public void BuildArguments_ForceCopy_ContainsForceCopyCommand()
    {
        var job = CreateTestJob(CopyMode.ForceCopy);
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/cmd=force_copy", args);
    }

    [Fact]
    public void BuildArguments_Move_ContainsMoveCommand()
    {
        var job = CreateTestJob(CopyMode.Move);
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/cmd=move", args);
    }

    [Fact]
    public void BuildArguments_UsesGlobalBufferSize_WhenNoOverride()
    {
        var job = CreateTestJob();
        job.Options.BufferSizeMb = null;
        var settings = CreateTestSettings();
        settings.DefaultBufferSizeMb = 512;

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/bufsize=512m", args);
    }

    [Fact]
    public void BuildArguments_UsesJobBufferSize_WhenOverridden()
    {
        var job = CreateTestJob();
        job.Options.BufferSizeMb = 128;
        var settings = CreateTestSettings();
        settings.DefaultBufferSizeMb = 256;

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/bufsize=128m", args);
    }

    [Fact]
    public void BuildArguments_UsesGlobalSpeed_WhenNoOverride()
    {
        var job = CreateTestJob();
        job.Options.Speed = null;
        var settings = CreateTestSettings();
        settings.DefaultSpeed = "autoslow";

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/speed=autoslow", args);
    }

    [Fact]
    public void BuildArguments_UsesJobSpeed_WhenOverridden()
    {
        var job = CreateTestJob();
        job.Options.Speed = "5";
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/speed=5", args);
    }

    [Fact]
    public void BuildArguments_IncludesAllSources()
    {
        var job = CreateTestJob();
        job.SourcePaths = new List<string> { @"D:\Source1", @"D:\Source2", @"E:\Source3" };
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("\"D:\\Source1\"", args);
        Assert.Contains("\"D:\\Source2\"", args);
        Assert.Contains("\"E:\\Source3\"", args);
    }

    [Fact]
    public void BuildArguments_IncludesDestinationWithTrailingBackslash()
    {
        var job = CreateTestJob();
        job.DestinationPath = @"\\NAS\share\dest";
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains(@"/to=""\\NAS\share\dest\""", args);
    }

    [Fact]
    public void BuildArguments_DestinationAlreadyHasTrailingBackslash()
    {
        var job = CreateTestJob();
        job.DestinationPath = @"\\NAS\share\dest\";
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        // Should not double the backslash
        Assert.Contains(@"/to=""\\NAS\share\dest\""", args);
    }

    [Fact]
    public void BuildArguments_IncludesRequiredFlags()
    {
        var job = CreateTestJob();
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains("/log", args);
        Assert.Contains("/auto_close", args);
        Assert.Contains("/no_confirm", args);
        Assert.Contains("/error_stop", args);
    }

    [Fact]
    public void BuildArguments_IncludesLogFile_WhenSet()
    {
        var job = CreateTestJob();
        job.LogFilePath = @"C:\logs\job1.log";
        var settings = CreateTestSettings();

        var args = _service.BuildArguments(job, settings);

        Assert.Contains(@"/logfile=""C:\logs\job1.log""", args);
    }

    #endregion

    #region ParseOutputLine Tests

    [Fact]
    public void ParseOutputLine_FullLine_ParsesAllFields()
    {
        var line = "Total: 42,345,678,912 / 56,789,012,345 bytes  (74.6%)  85.3 MB/s  ETA 02:41";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(74.6, result.ProgressPercent);
        Assert.Equal(85.3, result.SpeedMbps);
        Assert.Equal("02:41", result.TimeRemaining);
        Assert.Equal(42345678912L, result.TransferredBytes);
        Assert.Equal(56789012345L, result.TotalBytes);
    }

    [Fact]
    public void ParseOutputLine_ProgressOnly()
    {
        var line = "Processing (50.0%)";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(50.0, result.ProgressPercent);
    }

    [Fact]
    public void ParseOutputLine_WithSpeed()
    {
        var line = "Transfer (30.5%)  120.7 MB/s";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(30.5, result.ProgressPercent);
        Assert.Equal(120.7, result.SpeedMbps);
    }

    [Fact]
    public void ParseOutputLine_WithRemainingTime()
    {
        var line = "Copy (90.2%)  45.1 MB/s  Remaining 01:30:45";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(90.2, result.ProgressPercent);
        Assert.Equal("01:30:45", result.TimeRemaining);
    }

    [Fact]
    public void ParseOutputLine_100Percent()
    {
        var line = "Total: 56,789,012,345 / 56,789,012,345 bytes  (100%)  0.0 MB/s";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(100.0, result.ProgressPercent);
        Assert.Equal(56789012345L, result.TransferredBytes);
        Assert.Equal(56789012345L, result.TotalBytes);
    }

    [Fact]
    public void ParseOutputLine_EmptyLine_ReturnsNull()
    {
        var result = _service.ParseOutputLine("");
        Assert.Null(result);
    }

    [Fact]
    public void ParseOutputLine_WhitespaceLine_ReturnsNull()
    {
        var result = _service.ParseOutputLine("   ");
        Assert.Null(result);
    }

    [Fact]
    public void ParseOutputLine_NoProgress_ReturnsNull()
    {
        var line = "FastCopy v3.x starting...";

        var result = _service.ParseOutputLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseOutputLine_IntegerPercent()
    {
        var line = "Processing (75%)";

        var result = _service.ParseOutputLine(line);

        Assert.NotNull(result);
        Assert.Equal(75.0, result.ProgressPercent);
    }

    #endregion

    #region Helper Methods

    private static CopyJob CreateTestJob(CopyMode mode = CopyMode.Differential)
    {
        return new CopyJob
        {
            Id = Guid.NewGuid(),
            SourcePaths = new List<string> { @"D:\TestSource" },
            DestinationPath = @"\\NAS\dest",
            Mode = mode,
            Options = new JobOptions()
        };
    }

    private static AppSettings CreateTestSettings()
    {
        return new AppSettings
        {
            FastCopyExePath = @"C:\Program Files\FastCopy\FastCopy.exe",
            DefaultBufferSizeMb = 256,
            DefaultSpeed = "full"
        };
    }

    #endregion
}
