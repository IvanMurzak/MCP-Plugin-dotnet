

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class RequestToolCompletedData
    {
        public string RequestId { get; set; } = string.Empty;
        public ResponseCallTool Result { get; set; } = null!;

        public override string ToString()
            => $"RequestId: {RequestId}, Result: {Result}";
    }
}