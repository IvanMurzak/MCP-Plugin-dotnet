string? transport = null;
string? logFile = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--transport" && i + 1 < args.Length)
    {
        transport = args[++i];
    }
    else if (args[i] == "--log-file" && i + 1 < args.Length)
    {
        logFile = args[++i];
    }
}

Console.WriteLine($"DummyServer started with transport: {transport}, logFile: {logFile}");
