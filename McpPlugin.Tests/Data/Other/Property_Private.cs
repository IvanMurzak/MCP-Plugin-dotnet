// Private property
namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Property_Private
    {
        private int Secret { get; set; }

        public void SetSecret(int value) => Secret = value;
        public int GetSecret() => Secret;
    }
}
