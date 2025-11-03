

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class RequestResourcesUpdated
    {
        public string RequestId { get; set; } = string.Empty;

        public override string ToString()
            => $"RequestId: {RequestId}";
    }
}