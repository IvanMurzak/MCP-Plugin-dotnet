// Async method returning Task
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Method_Async_Task
    {
        public async Task DoAsync()
        {
            await Task.Yield();
        }
    }
}
