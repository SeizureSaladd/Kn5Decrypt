namespace Kn5Decrypt;

internal static class Program
{
    private static int Main(string[] args)
    {
        return args.Length == 0 ? RunInteractive() : Dispatch(args);
    }

    private static int Dispatch(string[] args)
    {
        var verb = args[0].ToLowerInvariant();
        var rest = args[1..];
        try
        {
            switch (verb)
            {
                case "decrypt":
                    if (rest.Length < 1) return Usage("decrypt <file.kn5> [outDir]");
                    Kn5CspDecryptor.Run(rest[0], rest.Length >= 2 ? rest[1] : null);
                    return 0;
                case "acd":
                    if (rest.Length < 2) return Usage("acd <data.acd> <outDir>");
                    AcdUnpacker.Run(rest[0], rest[1]);
                    return 0;
                case "unprotect":
                    if (rest.Length < 1) return Usage("unprotect <file.kn5>");
                    Kn5Protection.Run(rest[0]);
                    return 0;
                case "-h":
                case "--help":
                case "help":
                    PrintHelp();
                    return 0;
                default:
                    Ui.Error($"Unknown command '{verb}'.");
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex.Message);
            return 1;
        }
    }

    private static int Usage(string sub)
    {
        Ui.Error($"Usage: Kn5Decrypt {sub}");
        return 2;
    }

    private static void PrintHelp()
    {
        Ui.Banner("Kn5Decrypt", "KN5 and data.acd toolkit");
        Ui.Plain("Usage:");
        Ui.Detail("Kn5Decrypt decrypt <file.kn5> [outDir]");
        Ui.Detail("\tDecrypt a CSP-protected KN5, export recovered assets, and rebuild the KN5 when possible.");
        Ui.Detail("Kn5Decrypt acd <data.acd> <outDir>");
        Ui.Detail("\tUnpack and decrypt a data.acd archive.");
        Ui.Detail("Kn5Decrypt unprotect <file.kn5>");
        Ui.Detail("\tRemoves KN5 unpack protection. Writes a .bak backup file and removes protection in-place.");
        Ui.Detail("Kn5Decrypt");
        Ui.Detail("\tOpen the interactive menu.");
    }

    private static int RunInteractive()
    {
        while (true)
        {
            Ui.Banner("Kn5Decrypt", "Pick an action. Enter 0 or q to leave the tool.");
            Ui.MenuOption("1", "Decrypt a CSP-protected KN5");
            Ui.MenuOption("2", "Unpack a data.acd archive");
            Ui.MenuOption("3", "Remove KN5 unpack protection");
            Ui.MenuOption("0", "Quit");
            Ui.Prompt("Choice");
            var choice = Console.ReadLine()?.Trim();
            if (choice == null || choice == "0" || choice.Equals("q", StringComparison.OrdinalIgnoreCase))
                return 0;

            try
            {
                switch (choice)
                {
                    case "1":
                    {
                        var kn5 = Prompt("KN5 path");
                        var outArg = PromptOptional("Output dir (blank = <name>_decrypted)");
                        Kn5CspDecryptor.Run(kn5, outArg);
                        break;
                    }
                    case "2":
                    {
                        var acd = Prompt("data.acd path");
                        var outDir = Prompt("Output dir");
                        AcdUnpacker.Run(acd, outDir);
                        break;
                    }
                    case "3":
                    {
                        var kn5 = Prompt("KN5 path (will be patched in place; .bak written)");
                        Kn5Protection.Run(kn5);
                        break;
                    }
                    default:
                        Ui.Warn("Choose 1, 2, 3, or 0.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Ui.Error(ex.Message);
            }
        }
    }

    private static string Prompt(string label)
    {
        while (true)
        {
            Ui.Prompt(label);
            var s = Console.ReadLine();
            if (s == null) throw new OperationCanceledException();
            s = s.Trim().Trim('"');
            if (s.Length > 0) return s;
            Ui.Warn("A value is required here.");
        }
    }

    private static string? PromptOptional(string label)
    {
        Ui.Prompt(label);
        var s = Console.ReadLine();
        if (s == null) return null;
        s = s.Trim().Trim('"');
        return s.Length == 0 ? null : s;
    }
}
