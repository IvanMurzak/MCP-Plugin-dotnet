using System.ComponentModel;
using com.IvanMurzak.McpPlugin;
using System.IO;

[AiToolType]
public static class DummyTools
{
    public static string LogFile { get; set; } = "";

    [AiTool("ping", "Ping Tool")]
    [Description("Ping Tool")]
    public static string Ping()
    {
        if (!string.IsNullOrEmpty(LogFile))
        {
            File.AppendAllText(LogFile, "[EVENT] Tool Call: ping\n");
        }
        return "pong";
    }
}
