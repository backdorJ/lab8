using System.Collections.Concurrent;

namespace MultithreadingLab;

/// <summary>
/// Один producer кладёт объекты в блокирующую очередь, несколько воркеров
/// забирают их и обрабатывают. Остановка: очередь опустела после CompleteAdding
/// либо отмена по CancellationToken (любая клавиша).
/// </summary>
internal static class ProducerConsumerDemo
{
    private const int WorkerCount = 3;
    private const int MinItems = 50;
    private const int MaxItems = 100;
    private const int QueueCapacity = 10;
    private const int ProcessingDelayMs = 100;

    public static async Task RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("=== Задание 2. Producer–Consumer ===");
        Console.WriteLine($"Воркеров: {WorkerCount}. Элементов: случайно от {MinItems} до {MaxItems}.");
        Console.WriteLine($"Очередь: BlockingCollection, ёмкость {QueueCapacity}. Producer блокируется, если очередь полна.");
        Console.WriteLine("Обработка элемента: число * 2, затем пауза 100 мс.");
        Console.WriteLine("Остановка: воркеры завершаются, когда очередь опустеет.");
        Console.WriteLine("Либо нажмите любую клавишу — обработка прервётся через CancellationToken.");
        Console.WriteLine();

        using var queue = new BlockingCollection<WorkItem>(QueueCapacity);
        using var cancellation = new CancellationTokenSource();
        using var stopWatcher = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;

        Task keyWatcher = Task.Run(() => WatchKey(cancellation, stopWatcher.Token));
        var processed = 0;

        Task<int>[] workers = Enumerable.Range(1, WorkerCount)
            .Select(id => Task.Run(() => Consume(id, queue, token, ref processed)))
            .ToArray();

        Task<int> producer = Task.Run(() => Produce(queue, token));

        try
        {
            await Task.WhenAll(workers.Cast<Task>().Append(producer));
        }
        catch (OperationCanceledException)
        {
            // Отмена — штатный сценарий, итог печатается ниже.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Непредвиденная ошибка: {ex.Message}");
        }

        stopWatcher.Cancel();
        try
        {
            await keyWatcher;
        }
        catch (OperationCanceledException)
        {
        }

        int produced = producer.Status == TaskStatus.RanToCompletion ? producer.Result : 0;

        Console.WriteLine();
        Console.WriteLine("--- Итог ---");
        Console.WriteLine($"Добавлено в очередь: {produced}");
        Console.WriteLine($"Обработано воркерами: {Volatile.Read(ref processed)}");
        Console.WriteLine(cancellation.IsCancellationRequested
            ? "Причина остановки: CancellationToken (нажата клавиша)."
            : "Причина остановки: очередь опустела, producer вызвал CompleteAdding.");
    }

    private static int Produce(BlockingCollection<WorkItem> queue, CancellationToken token)
    {
        int count = Random.Shared.Next(MinItems, MaxItems + 1);
        int added = 0;

        Console.WriteLine($"[Producer] будет добавлено объектов: {count}");

        try
        {
            for (int id = 1; id <= count; id++)
            {
                token.ThrowIfCancellationRequested();
                var item = new WorkItem(id, id);

                if (!queue.TryAdd(item, 0, token))
                {
                    Console.WriteLine($"[Producer] очередь полна ({QueueCapacity}), поток блокируется...");
                    queue.Add(item, token);
                }

                added++;
                Console.WriteLine($"[Producer] в очередь: объект #{item.Id}, число {item.Number}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Producer] отмена: новые объекты не добавляются.");
        }
        finally
        {
            // После CompleteAdding воркеры дочитывают хвост и GetConsumingEnumerable завершается.
            // При отмене токен прерывает их, даже если в очереди ещё есть элементы.
            queue.CompleteAdding();
        }

        return added;
    }

    private static int Consume(
        int workerId,
        BlockingCollection<WorkItem> queue,
        CancellationToken token,
        ref int processed)
    {
        int local = 0;
        try
        {
            foreach (WorkItem item in queue.GetConsumingEnumerable(token))
            {
                int result = item.Number * 2;
                Thread.Sleep(ProcessingDelayMs);
                local++;
                Interlocked.Increment(ref processed);
                Console.WriteLine(
                    $"[Worker {workerId} | поток {Environment.CurrentManagedThreadId}] " +
                    $"объект #{item.Id}: {item.Number} * 2 = {result}");
            }

            Console.WriteLine($"[Worker {workerId}] очередь пуста, поток завершён.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[Worker {workerId}] остановлен по CancellationToken. Обработано этим потоком: {local}.");
        }

        return local;
    }

    private static void WatchKey(CancellationTokenSource cancellation, CancellationToken stopWatching)
    {
        if (Console.IsInputRedirected)
            return;

        while (!stopWatching.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                    Console.WriteLine();
                    Console.WriteLine("Клавиша нажата: запрос отмены через CancellationToken.");
                    cancellation.Cancel();
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            Thread.Sleep(40);
        }
    }

    private sealed class WorkItem
    {
        public WorkItem(int id, int number)
        {
            Id = id;
            Number = number;
        }

        public int Id { get; }
        public int Number { get; }
    }
}
