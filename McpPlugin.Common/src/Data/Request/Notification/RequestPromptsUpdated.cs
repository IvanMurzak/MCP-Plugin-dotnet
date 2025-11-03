

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class RequestPromptsUpdated
    {
        public string RequestId { get; set; } = string.Empty;

        public override string ToString()
            => $"RequestId: {RequestId}";
    }
}