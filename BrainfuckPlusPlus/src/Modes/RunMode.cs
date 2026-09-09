using System.Diagnostics;

namespace Brainfuck.Modes;

public class RunMode : Mode
{
    public string Keyword => "run";

    public void Execute(ReadOnlySpan<string> args)
    {
        BuildMode.ExecuteBuild(args, result =>
        {
            if (result.RedirectLaunch)
            {
                result.IO.SendLaunchRedirect(new BuildMode.LaunchCommand()
                {
                    exe = result.ExecutablePath,
                    cwd = result.ProjectSettings.projectDir,
                    args = result.ProjectSettings.launchSettings.args
                });
                return;
            }

            var startInfo = new ProcessStartInfo(result.ExecutablePath)
            {
                WorkingDirectory = result.ProjectSettings.projectDir
            };
            foreach (var arg in result.ProjectSettings.launchSettings.args)
                startInfo.ArgumentList.Add(arg);

            var proc = Process.Start(startInfo);
            if (proc is null)
            {
                result.IO.WriteLog("Failed to run build output");
                return;
            }

            result.IO.ShouldExit += () =>
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            };

            proc.WaitForExit();
        }, allowRedirectLaunch: true);
    }
}
