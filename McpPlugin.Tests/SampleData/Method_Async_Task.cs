// Async method returning Task
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Common.Tests.SampleData
{
    public class Method_Async_Task
    {
        public async Task DoAsync()
        {
            await Task.Yield();
        }
    }
}
