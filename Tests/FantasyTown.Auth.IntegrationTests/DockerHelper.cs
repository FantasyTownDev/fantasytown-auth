using System.Diagnostics;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// Docker 可用性检测
/// </summary>
public static class DockerHelper
{
    public static bool IsAvailable()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
