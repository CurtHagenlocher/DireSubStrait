using Antlr4.Runtime;
using SparkSubstrait;
using Xunit;

namespace SparkSubstraitTests
{
    public class UnitTest1
    {
        [Fact]
        public void SmokeTest()
        {
            AntlrInputStream stream = new AntlrInputStream("`A` = 1");
            SqlBaseLexer lexer = new SqlBaseLexer(stream);
            CommonTokenStream tokens = new CommonTokenStream(lexer);
            SqlBaseParser parser = new SqlBaseParser(tokens);

            var result = parser.booleanExpression();
            new SqlVisitor().Visit(result);
        }

        [Fact]
        public void Select1()
        {
            Translator.Translate("SELECT 1");
        }

        class SqlVisitor : SqlBaseParserBaseVisitor<object>
        {
            public override object VisitBooleanExpression(SqlBaseParser.BooleanExpressionContext context)
            {
                return base.VisitBooleanExpression(context);
            }

            public override object VisitArithmeticBinary(SqlBaseParser.ArithmeticBinaryContext context)
            {
                return base.VisitArithmeticBinary(context);
            }
        }
    }
}
