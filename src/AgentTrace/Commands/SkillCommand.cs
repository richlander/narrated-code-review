using System.Reflection;

namespace AgentTrace.Commands;

/// <summary>
/// Prints the embedded SKILL.md to stdout — plain text, no ANSI.
/// Designed for LLMs to learn how to use this tool.
/// </summary>
public static class SkillCommand
{
    public static void Execute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("AgentTrace.SKILL.md");

        if (stream == null)
        {
            Console.Error.WriteLine("SKILL.md resource not found.");
            return;
        }

        using var reader = new StreamReader(stream);
        Console.Out.Write(reader.ReadToEnd());
    }
}
