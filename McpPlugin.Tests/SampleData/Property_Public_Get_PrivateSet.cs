// Public getter, private setter property
namespace com.IvanMurzak.McpPlugin.Common.Tests.SampleData
{
    public class Property_Public_Get_PrivateSet
    {
        public string Name { get; private set; } = string.Empty;

        public void SetName(string name)
        {
            Name = name;
        }
    }
}
