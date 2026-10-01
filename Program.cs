using System.Globalization;
using System.Text;

namespace MultithreadingLab;

internal static class Program
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Консоль без перенастраиваемой кодировки — оставляем текущую.
        }

        if (args.Length == 0)
            return await RunMenuAsync();

        return await RunFromArgsAsync(args);
    }

    private static async Task<int> RunMenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Лабораторная работа: Многопоточность в C#");
            Console.WriteLine("1 — Гонка данных (UnsafeIncrement / lock / Interlocked)");
            Console.WriteLine("2 — Producer–Consumer (BlockingCollection)");
            Console.WriteLine("0 — Выход");
            Console.WriteLine();
            Console.WriteLine("Задание 1 из командной строки: dotnet run -- <потоки> <цель>");
            Console.WriteLine("Пример: dotnet run -- 8 1000000");
            Console.Write("Выбор: ");

            string? choice = Console.ReadLine()?.Trim();
            switch (choice)
            {
                case "1":
                    RunRaceInteractive();
                    Pause();
                    break;
                case "2":
                    await ProducerConsumerDemo.RunAsync();
                    Pause();
                    break;
                case "0":
                    return 0;
                default:
                    Console.WriteLine("Неизвестный пункт. Введите 1, 2 или 0.");
                    break;
            }
        }
    }

    private static async Task<int> RunFromArgsAsync(string[] args)
    {
        if (IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        // «2» — это пункт меню. Пара «2 1000000» означает гонку: 2 потока и цель 1000000.
        if (args[0] is "pc" or "producer" or "queue" || (args.Length == 1 && args[0] == "2"))
        {
            await ProducerConsumerDemo.RunAsync();
            return 0;
        }

        string[] raceArgs = IsRaceMode(args[0]) ? args.Skip(1).ToArray() : args;
        if (raceArgs.Length < 2
            || !TryParseInt(raceArgs[0], out int threads)
            || !TryParseInt(raceArgs[1], out int target))
        {
            PrintUsage();
            return 1;
        }

        if (!RaceConditionDemo.TryValidate(threads, target, out string? error))
        {
            Console.WriteLine(error);
            return 1;
        }

        bool? stepsFlag = ParseStepsFlag(raceArgs.Skip(2));
        bool verbose = stepsFlag ?? AskYesNo("Выводить шаги инкремента? (д/н): ");
        verbose = ConfirmVerbose(verbose, target);

        RaceConditionDemo.Run(threads, target, verbose);
        return 0;
    }

    private static void RunRaceInteractive()
    {
        int threads = ReadInt(
            "Количество потоков (1..256): ",
            RaceConditionDemo.MinThreads,
            RaceConditionDemo.MaxThreads);
        int target = ReadInt(
            "До какого значения доводить счётчик (1..100000000): ",
            RaceConditionDemo.MinTarget,
            RaceConditionDemo.MaxTarget);

        bool verbose = AskYesNo("Выводить шаги инкремента? (д/н): ");
        verbose = ConfirmVerbose(verbose, target);
        RaceConditionDemo.Run(threads, target, verbose);
    }

    private static bool ConfirmVerbose(bool verbose, int target)
    {
        if (!verbose || target <= 5_000)
            return verbose;

        Console.WriteLine(
            $"Будет выведено около {(target * 3).ToString("N0", Ru)} строк. " +
            "Замер времени включит вывод в консоль и перестанет отражать скорость инкремента.");
        return AskYesNo("Всё равно выводить шаги? (д/н): ");
    }

    private static bool? ParseStepsFlag(IEnumerable<string> flags)
    {
        foreach (string flag in flags)
        {
            if (flag is "--steps" or "-v" or "steps" or "д")
                return true;
            if (flag is "--no-steps" or "nosteps" or "н")
                return false;
        }

        return null;
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "/?" or "help";

    private static bool IsRaceMode(string arg) =>
        arg is "1" or "race";

    private static void PrintUsage()
    {
        Console.WriteLine("Использование:");
        Console.WriteLine("  MultithreadingLab <потоки> <цель> [--steps|--no-steps]");
        Console.WriteLine("  MultithreadingLab race <потоки> <цель>");
        Console.WriteLine("  MultithreadingLab pc");
        Console.WriteLine();
        Console.WriteLine("Без аргументов открывается меню.");
        Console.WriteLine("Пример: dotnet run -- 8 1000000");
    }

    private static int ReadInt(string prompt, int min, int max)
    {
        while (true)
        {
            Console.Write(prompt);
            string? line = Console.ReadLine();
            if (TryParseInt(line, out int value) && value >= min && value <= max)
                return value;

            Console.WriteLine($"Введите целое число от {min.ToString("N0", Ru)} до {max.ToString("N0", Ru)}.");
        }
    }

    private static bool AskYesNo(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            string? redirected = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(redirected))
                return false;
            return IsYes(redirected);
        }

        while (true)
        {
            Console.Write(prompt);
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine("Введите д или н.");
                continue;
            }

            if (IsYes(line))
                return true;
            if (IsNo(line))
                return false;

            Console.WriteLine("Введите д или н.");
        }
    }

    private static bool IsYes(string text)
    {
        string value = text.Trim().ToLowerInvariant();
        return value is "y" or "yes" or "д" or "да" or "1";
    }

    private static bool IsNo(string text)
    {
        string value = text.Trim().ToLowerInvariant();
        return value is "n" or "no" or "н" or "нет" or "0";
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = text.Trim()
            .Replace(" ", "")
            .Replace("\u00A0", "")
            .Replace("_", "");

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void Pause()
    {
        if (Console.IsInputRedirected)
            return;

        Console.WriteLine();
        Console.Write("Enter — вернуться в меню...");
        Console.ReadLine();
    }
}
