using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System;
using System.Collections.Generic;
using System.Text;

namespace SparkSubstrait
{
    public class Translator
    {
        public static void Translate(string text)
        {
            AntlrInputStream stream = new AntlrInputStream(text);
            SqlBaseLexer lexer = new SqlBaseLexer(stream);
            CommonTokenStream tokens = new CommonTokenStream(lexer);
            SqlBaseParser parser = new SqlBaseParser(tokens);

            var result = parser.statement();
            new SqlVisitor().Visit(result);
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

            public override object VisitConstant([NotNull] SqlBaseParser.ConstantContext context)
            {
                return base.VisitConstant(context);
            }
        }
    }
}
