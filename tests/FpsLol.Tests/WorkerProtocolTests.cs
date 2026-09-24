using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.Optimizations.Privileged;
using FpsLol.SystemIntegration;
using Xunit;

namespace FpsLol.Tests;

/// <summary>
/// End-to-end test of the helper-process channel used for elevated operations. The helper is started
/// WITHOUT elevation here (no UAC prompt), so it can only run per-user jobs — which is enough to verify
/// the pipe protocol, the parent-PID check, job execution and result mapping.
/// </summary>
[Trait("Category", "System")]
[Collection("System")]
public class WorkerProtocolTests
{
    private static string AppExe()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var config in new[] { "Debug", "Release" })
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, @"..\..\..\..\..\src\FpsLol\bin", config, @"net8.0-windows\FPS.LOL.exe"));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Build src/FpsLol first.");
    }

    [Fact]
    public async Task Helper_executes_jobs_and_returns_results()
    {
        const string path = @"Software\Microsoft\GameBar";
        const string name = "FpsLolWorkerTest";
        RegistryUtil.DeleteValue(RegRoot.CurrentUser, path, name);

        var pipeName = "fpslol-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var helper = Process.Start(new ProcessStartInfo(AppExe(), $"--elevated-worker {pipeName} {Environment.ProcessId}") { UseShellExecute = false })!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await server.WaitForConnectionAsync(cts.Token);

        var apply = new ApplyJob { Title = "apply", Actions = [new RegistryValueAction(RegRoot.CurrentUser, path, name, RegValue.Dword(7))] };
        var blocked = new ApplyJob { Title = "blocked", Actions = [new RegistryValueAction(RegRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", RegValue.Dword(1))] };
        await PipeFraming.WriteAsync(server, new WorkerRequest { UserSid = Elevation.CurrentUserSid, Jobs = [apply, blocked] }, cts.Token);
        var response = await PipeFraming.ReadAsync<WorkerResponse>(server, cts.Token);
        await helper.WaitForExitAsync(cts.Token);

        var applied = response.Results.Single(r => r.Key == apply.Key);
        Assert.Equal(OperationStatus.Success, applied.Status);
        Assert.Single(applied.Snapshots);
        Assert.Equal(7L, RegistryUtil.ReadNumber(RegRoot.CurrentUser, path, name));

        var refused = response.Results.Single(r => r.Key == blocked.Key);
        Assert.Equal(OperationStatus.Failed, refused.Status); // safety policy holds inside the helper too

        // Restore with the snapshot returned by the helper.
        Assert.Equal(OperationStatus.Success, TransactionRunner.Restore(applied.Snapshots).Status);
        Assert.Null(RegistryUtil.Read(RegRoot.CurrentUser, path, name));
        Assert.Equal(0, helper.ExitCode);
    }

    [Fact]
    public async Task Helper_refuses_a_server_that_is_not_its_parent()
    {
        var pipeName = "fpslol-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        // Claim a different parent PID: the helper must disconnect without reading any job.
        using var helper = Process.Start(new ProcessStartInfo(AppExe(), $"--elevated-worker {pipeName} 4") { UseShellExecute = false })!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await server.WaitForConnectionAsync(cts.Token);
        await helper.WaitForExitAsync(cts.Token);
        Assert.Equal(3, helper.ExitCode);
    }
}
