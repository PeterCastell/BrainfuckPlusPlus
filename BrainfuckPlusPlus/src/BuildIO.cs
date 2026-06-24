using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Brainfuck.Modes;

namespace Brainfuck;

public interface BuildIO: IDisposable
{
    public void SendLaunchRedirect(BuildMode.LaunchCommand launch);
    public void WriteLog(string line);
    public void WriteErr(string line);
    public event Action? ShouldExit;
}
public class StandardBuildIO : BuildIO
{
    public void WriteLog(string line) => Console.WriteLine(line);
    public void WriteErr(string line) => Console.Error.WriteLine(line);

    public void Dispose() => GC.SuppressFinalize(this);

    public void SendLaunchRedirect(BuildMode.LaunchCommand launch)
    {
        string launchCommand = JsonSerializer.Serialize(launch);
        WriteLog("---" + launchCommand);
    }
#pragma warning disable CS0067
    public event Action? ShouldExit;
    #pragma warning restore CS0067
}
public class TCPBuildIO : BuildIO
{
    readonly TcpClient client;
    readonly NetworkStream stream;
    public TCPBuildIO(int port)
    {
        client = new TcpClient();
        client.Connect("127.0.0.1", port);
        stream = client.GetStream();
        _ = Task.Run(() =>
        {
            while (client.GetStream().ReadByte() != -1) { }
            ShouldExit?.Invoke();
        });
    }

    public void WriteLog(string line) => Console.WriteLine(line);
    public void WriteErr(string line) => Console.Error.WriteLine(line);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        client.Dispose();
    }

    public void SendLaunchRedirect(BuildMode.LaunchCommand launch)
    {
        string launchCommand = JsonSerializer.Serialize(launch);
        stream.Write(Encoding.ASCII.GetBytes(launchCommand));
    }

    public event Action? ShouldExit;
}