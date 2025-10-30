// Async method returning Task<int>
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Method_Async_TaskOfInt
    {
        public async Task<int> ComputeAsync(int a, int b)
        {
            await Task.Yield();
            return a + b;
        }
    }
}
