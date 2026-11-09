internal sealed class ConsoleProgress : IDisposable
{
    private int _previousLength;

    public ConsoleProgress()
    {
        Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    private void Clear()
    {
        Console.WriteLine();
        _previousLength = 0;
    }

    public void Write(int current, int total)
    {
        string message = $"Progress: {current}/{total} ({current * 100 / total}%)";
        Console.Write($"\r{message.PadRight(_previousLength)}");
        _previousLength = message.Length;
    }
}
