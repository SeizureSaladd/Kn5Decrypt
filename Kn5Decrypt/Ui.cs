namespace Kn5Decrypt;

internal static class Ui
{
    private static readonly bool SupportsColor = !Console.IsOutputRedirected && !Console.IsErrorRedirected;

    public static void Banner(string title, string subtitle)
    {
        Console.Out.WriteLine();
        WriteAccent(title, ConsoleColor.Cyan);
        Detail(subtitle);
    }

    public static void Plain(string message) => Console.Out.WriteLine(message);

    public static void Info(string message) => WriteLabeled(Console.Out, "info", message, ConsoleColor.Cyan);

    public static void Success(string message) => WriteLabeled(Console.Out, " ok ", message, ConsoleColor.Green);

    public static void Warn(string message) => WriteLabeled(Console.Out, "warn", message, ConsoleColor.Yellow);

    public static void Error(string message) => WriteLabeled(Console.Error, "error", message, ConsoleColor.Red);

    public static void Detail(string message)
    {
        if (!SupportsColor)
        {
            Console.Out.WriteLine($"  {message}");
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Out.Write("  ");
        Console.ForegroundColor = previous;
        Console.Out.WriteLine(message);
    }

    public static void MenuOption(string key, string description)
    {
        if (!SupportsColor)
        {
            Console.Out.WriteLine($"  {key}  {description}");
            return;
        }

        var previous = Console.ForegroundColor;
        Console.Out.Write("  ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Out.Write(key);
        Console.ForegroundColor = previous;
        Console.Out.WriteLine($"  {description}");
    }

    public static void Prompt(string label)
    {
        if (!SupportsColor)
        {
            Console.Write($"{label}: ");
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(label);
        Console.ForegroundColor = previous;
        Console.Write(": ");
    }

    private static void WriteLabeled(TextWriter writer, string label, string message, ConsoleColor color)
    {
        if (!SupportsColor)
        {
            writer.WriteLine($"[{label}] {message}");
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        writer.Write($"[{label}] ");
        Console.ForegroundColor = previous;
        writer.WriteLine(message);
    }

    private static void WriteAccent(string message, ConsoleColor color)
    {
        if (!SupportsColor)
        {
            Console.Out.WriteLine(message);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Out.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
