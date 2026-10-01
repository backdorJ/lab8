using System.Diagnostics;
using System.Globalization;

namespace MultithreadingLab;

/// <summary>
/// Несколько потоков инкрементируют общий счётчик тремя способами:
/// обычный counter++, lock и Interlocked.Increment.
/// </summary>
internal static class RaceConditionDemo
{
    public const int MinThreads = 1;
    public const int MaxThreads = 256;
    public const int MinTarget = 1;
    public const int MaxTarget = 100_000_000;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly object Sync = new();
    private static readonly object ConsoleSync = new();

    private static int _counter;
    private static bool _printSteps;

    public static bool TryValidate(int threadCount, int target, out string? error)
    {
        if (threadCount < MinThreads || threadCount > MaxThreads)
        {
            error = $"Количество потоков должно быть от {MinThreads} до {MaxThreads}.";
            return false;
        }

        if (target < MinTarget || target > MaxTarget)
        {
            error = $"Цель должна быть от {MinTarget.ToString("N0", Ru)} до {MaxTarget.ToString("N0", Ru)}.";
            return false;
        }

        error = null;
        return true;
    }

    public static void Run(int threadCount, int target, bool verbose)
    {
        if (!TryValidate(threadCount, target, out string? error))
            throw new ArgumentOutOfRangeException(nameof(target), error);

        int[] portions = SplitWork(threadCount, target);

        Console.WriteLine();
        Console.WriteLine("=== Задание 1. Гонка данных ===");
        Console.WriteLine($"Потоков: {threadCount.ToString("N0", Ru)}");
        Console.WriteLine($"Цель (сколько раз выполнить инкремент): {target.ToString("N0", Ru)}");
        Console.WriteLine("Итерации по потокам: " + string.Join(" + ", portions.Select(p => p.ToString("N0", Ru))));
        Console.WriteLine(verbose
            ? "Шаги: включены (время в таблице включает вывод в консоль)."
            : "Шаги: выключены.");
        Console.WriteLine();
        Console.WriteLine("UnsafeIncrement — counter++ без синхронизации: чтение, изменение и запись не атомарны, часть инкрементов теряется.");
        Console.WriteLine("lock — в критической секции инкремент выполняет только один поток.");
        Console.WriteLine("Interlocked.Increment — атомарный инкремент без захвата монитора.");
        Console.WriteLine();
        Console.WriteLine("Прогрев JIT (в таблицу не входит)...");

        _printSteps = false;
        Warmup(Math.Min(threadCount, 2));

        _printSteps = verbose;
        RaceRow unsafeRow = Measure("UnsafeIncrement", UnsafeIncrement, portions, target);
        RaceRow lockRow = Measure("lock", LockedIncrement, portions, target);
        RaceRow atomicRow = Measure("Interlocked.Increment", AtomicIncrement, portions, target);

        PrintTable([unsafeRow, lockRow, atomicRow]);

        if (unsafeRow.Counter == target)
        {
            Console.WriteLine();
            Console.WriteLine("На этом прогоне UnsafeIncrement случайно дал верный итог. Повторите запуск с большей целью — гонка проявится не каждый раз.");
        }
    }

    private static void Warmup(int threadCount)
    {
        int[] portions = SplitWork(Math.Max(threadCount, 1), 2_000);
        Measure("warmup-unsafe", UnsafeIncrement, portions, 2_000);
        Measure("warmup-lock", LockedIncrement, portions, 2_000);
        Measure("warmup-atomic", AtomicIncrement, portions, 2_000);
    }

    private static RaceRow Measure(string method, Action<int> increment, int[] portions, int expected)
    {
        _counter = 0;

        var threads = new Thread[portions.Length];
        for (int i = 0; i < portions.Length; i++)
        {
            int iterations = portions[i];
            threads[i] = new Thread(() => increment(iterations))
            {
                IsBackground = true,
                Name = $"{method}-{i + 1}"
            };
        }

        var stopwatch = Stopwatch.StartNew();
        foreach (Thread thread in threads)
            thread.Start();
        foreach (Thread thread in threads)
            thread.Join();
        stopwatch.Stop();

        int counter = Volatile.Read(ref _counter);
        return new RaceRow(method, stopwatch.Elapsed.TotalMilliseconds, counter, expected);
    }

    /// <summary>Простой counter++. Общий объект не блокируется.</summary>
    private static void UnsafeIncrement(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            _counter++;
            if (_printSteps)
                PrintStep("UnsafeIncrement", Volatile.Read(ref _counter));
        }
    }

    /// <summary>Инкремент в критической секции lock(obj).</summary>
    private static void LockedIncrement(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            int observed;
            lock (Sync)
            {
                observed = ++_counter;
            }

            if (_printSteps)
                PrintStep("lock", observed);
        }
    }

    /// <summary>Атомарный инкремент: Interlocked.Increment(ref counter).</summary>
    private static void AtomicIncrement(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            int observed = Interlocked.Increment(ref _counter);
            if (_printSteps)
                PrintStep("Interlocked.Increment", observed);
        }
    }

    private static void PrintStep(string method, int counter)
    {
        lock (ConsoleSync)
        {
            Console.WriteLine(
                $"[{method}] поток {Environment.CurrentManagedThreadId}: counter = {counter.ToString("N0", Ru)}");
        }
    }

    private static int[] SplitWork(int threadCount, int target)
    {
        var portions = new int[threadCount];
        int baseCount = target / threadCount;
        int remainder = target % threadCount;
        for (int i = 0; i < threadCount; i++)
            portions[i] = baseCount + (i < remainder ? 1 : 0);
        return portions;
    }

    private static void PrintTable(IReadOnlyList<RaceRow> rows)
    {
        string[] headers = ["Метод", "Время, мс", "Counter", "Ожидалось", "Потери"];
        string[][] cells = rows.Select(row => new[]
        {
            row.Method,
            row.Milliseconds.ToString("N2", Ru),
            row.Counter.ToString("N0", Ru),
            row.Expected.ToString("N0", Ru),
            (row.Expected - row.Counter).ToString("N0", Ru)
        }).ToArray();

        int[] widths = new int[headers.Length];
        for (int column = 0; column < headers.Length; column++)
        {
            widths[column] = headers[column].Length;
            foreach (string[] row in cells)
                widths[column] = Math.Max(widths[column], row[column].Length);
        }

        Console.WriteLine();
        PrintTableLine(headers, widths);
        Console.WriteLine(new string('-', widths.Sum() + (widths.Length - 1) * 3));
        foreach (string[] row in cells)
            PrintTableLine(row, widths);

        Console.WriteLine();
        Console.WriteLine("Потери = ожидалось − фактический counter. Для lock и Interlocked они должны быть равны 0.");
    }

    private static void PrintTableLine(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                Console.Write(" | ");

            // Числа выравниваем вправо, название метода — влево.
            Console.Write(i == 0
                ? cells[i].PadRight(widths[i])
                : cells[i].PadLeft(widths[i]));
        }

        Console.WriteLine();
    }

    private readonly record struct RaceRow(string Method, double Milliseconds, int Counter, int Expected);
}
