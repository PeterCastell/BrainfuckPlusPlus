
using System.Reflection;

namespace Brainfuck.Modes;

public interface Mode
{
    public string Keyword { get; }

    public void Execute(ReadOnlySpan<string> args);

    public static IEnumerable<Mode> GetModes()
    {
        return Assembly.GetExecutingAssembly().GetTypes().Where(type => type != typeof(Mode) && type.IsAssignableTo(typeof(Mode))).Select(type => (Mode?)Activator.CreateInstance(type)).WhereType<Mode?, Mode>();
    }
}