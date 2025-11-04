using System.ComponentModel;
using com.IvanMurzak.McpPlugin;

[McpPluginToolType]
public static class McpToolConsole
{
    [McpPluginTool("console-log", "Logs a message to the console.")]
    [Description("Logs a message to the console.")]
    public static void Log(string message)
    {
        Console.WriteLine(message);
    }
}