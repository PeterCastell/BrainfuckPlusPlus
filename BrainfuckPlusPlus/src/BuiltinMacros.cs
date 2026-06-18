using System.Reflection;

namespace Brainfuck;

public static class BuiltinMacros
{
    public record ASTInvocation(Macro Macro, StringSlice Argument) : AST.Token;
    
    [AttributeUsage(AttributeTargets.Field)]
    class MacroNameAttribute(string name) : Attribute
    {
        public string Name => name;
        public static readonly string[] AllNames = typeof(Macro).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.GetCustomAttribute<MacroNameAttribute>()!.Name).ToArray();
    }

    public static string GetMacroName(Macro macro) => MacroNameAttribute.AllNames[(int)macro];

    public enum Macro
    {
        [MacroName("import")] Import,
        [MacroName("embed_file")] EmbedFile
    }
}