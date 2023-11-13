using TSqlSubstrait;
using Xunit;

namespace TSqlSubstraitTests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            Translator.Translate("select 1");
        }
    }
}
