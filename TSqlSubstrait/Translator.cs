using Microsoft.SqlServer.TransactSql.ScriptDom;
using Substrait.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SubstraitType = Substrait.Protobuf.Type;

namespace TSqlSubstrait
{
    public class Translator
    {
        static readonly SubstraitType I32 = new SubstraitType() { I32 = new SubstraitType.Types.I32() { Nullability = SubstraitType.Types.Nullability.Required } };

        public static void Translate(string text)
        {
            var parser = new TSql160Parser(true);
            var fragment = ParseString(parser, text ?? "select * from [table] where upper(foo) = 'BAR'", out IList<ParseError> errors);

            new TestVisitor().VisitScript((TSqlScript)fragment);

            // SqlScriptGenerator generator = null;
        }

        public static TSqlFragment ParseString(TSqlParser parser, string source, out IList<ParseError> errors)
        {
            TSqlFragment fragment;
            using (StringReader sr = new StringReader(source))
            {
                fragment = parser.Parse(sr, out errors);
            }
            return fragment;
        }

        static Rel ReadTable(string name)
        {
            var result = new ReadRel { NamedTable = new ReadRel.Types.NamedTable() };
            result.NamedTable.Names.Add(name);
            result.BaseSchema = new NamedStruct { Struct = new SubstraitType.Types.Struct { Nullability = SubstraitType.Types.Nullability.Required } };
            return new Rel { Read = result };
        }

        class TestVisitor
        {
            public Rel VisitScript(TSqlScript script)
            {
                if (script?.Batches == null || script.Batches.Count != 1)
                {
                    throw new NotSupportedException(nameof(script));
                }
                return VisitBatch(script.Batches[0]);
            }

            Rel VisitBatch(TSqlBatch batch)
            {
                if (batch?.Statements == null || batch.Statements.Count != 1)
                {
                    throw new NotSupportedException(nameof(batch));
                }
                return VisitStatement(batch.Statements[0]);
            }

            Rel VisitStatement(TSqlStatement node)
            {
                switch (node)
                {
                    case SelectStatement selectStatement:
                        return VisitSelectStatement(selectStatement);
                    default:
                        throw new NotSupportedException(node.GetType().Name);
                }
            }

            Rel VisitSelectStatement(SelectStatement selectStatement)
            {
                if (selectStatement.QueryExpression != null)
                {
                    return VisitQueryExpression(selectStatement.QueryExpression);
                }
                throw new NotSupportedException(nameof(selectStatement));
            }

            Rel VisitQueryExpression(QueryExpression queryExpression)
            {
                switch (queryExpression)
                {
                    case QuerySpecification querySpecification:
                        return VisitQuerySpecification(querySpecification);
                    default:
                        throw new NotSupportedException(queryExpression.GetType().Name);
                }
            }

            Rel VisitQuerySpecification(QuerySpecification querySpec)
            {
                NotSupported(querySpec.ForClause, nameof(querySpec.ForClause));
                NotSupported(querySpec.OffsetClause, nameof(querySpec.OffsetClause));

                Rel table;
                if (querySpec.FromClause == null)
                {
                    // select literals?
                    table = SelectLiterals(querySpec.SelectElements);
                }
                else
                {
                    table = VisitFromClause(querySpec.FromClause);
                }

                if (querySpec.WhereClause != null)
                {
                    table = VisitWhereClause(table, querySpec.WhereClause);
                }

                // Projection

                return table;
            }

            Rel SelectLiterals(IList<SelectElement> elements)
            {
                string[] names = new string[elements.Count];
                var record = new Expression.Types.Literal.Types.Struct();

                for (int i = 0; i < elements.Count; i++)
                {
                    switch (elements[i])
                    {
                        case SelectScalarExpression scalarExpression:
                            if (scalarExpression.Expression is Literal literal)
                            {
                                names[i] = scalarExpression.ColumnName?.Value;
                                record.Fields.Add(VisitLiteral(literal));
                            }
                            else
                            {
                                goto default;
                            }
                            break;
                        default:
                            throw new NotImplementedException(elements[i].GetType().Name);
                    }
                }

                // TODO: return table type
                var result = new Rel { Read = new ReadRel { VirtualTable = new ReadRel.Types.VirtualTable() } };
                result.Read.VirtualTable.Values.Add(record);
                return result;
            }

            Rel VisitFromClause(FromClause fromClause)
            {
                if (fromClause?.TableReferences != null && fromClause.TableReferences.Count == 1)
                {
                    return VisitTableReference(fromClause.TableReferences[0]);
                }

                throw new NotSupportedException(nameof(fromClause));
            }

            Rel VisitTableReference(TableReference tableReference)
            {
                // JoinParenthesisTableReference, JoinTableReference, QueryDerivedTable, VariableTableReference, SchemaObjectFunctionTableReference

                if (tableReference is NamedTableReference namedTableRef)
                {
                    return VisitNamedTableReference(namedTableRef);
                }

                throw new NotSupportedException(tableReference.GetType().Name);
            }

            Rel VisitNamedTableReference(NamedTableReference namedTableReference)
            {
                return ReadTable(namedTableReference.SchemaObject.Identifiers[0].Value);
            }

            Rel VisitWhereClause(Rel from, WhereClause whereClause)
            {
                NotSupported(whereClause.Cursor, nameof(whereClause.Cursor));

                var expression = VisitBooleanExpression(whereClause.SearchCondition);
                return new Rel { Filter = new FilterRel { Input = from, Condition = expression } };
            }

            Expression VisitBooleanExpression(BooleanExpression expression)
            {
                switch (expression)
                {
                    case BooleanBinaryExpression binary:
                        return VisitBooleanBinaryExpression(binary);
                    case BooleanComparisonExpression comparison:
                        return VisitBooleanComparisonExpression(comparison);
                    case BooleanIsNullExpression isNull:
                        return VisitBooleanIsNullExpression(isNull);
                    case BooleanNotExpression not:
                        return VisitBooleanNotExpression(not);
                    case BooleanParenthesisExpression parenthesis:
                        return VisitBooleanParenthesisExpression(parenthesis);
                    case InPredicate inPredicate:
                        return VisitInPredicate(inPredicate);
                    default:
                        throw new NotSupportedException(expression.GetType().Name);
                }
            }

            Expression VisitBooleanBinaryExpression(BooleanBinaryExpression binary)
            {
                throw new NotImplementedException();
            }

            Expression VisitBooleanComparisonExpression(BooleanComparisonExpression comparison)
            {
                var left = VisitScalarExpression(comparison.FirstExpression);
                var right = VisitScalarExpression(comparison.SecondExpression);

                // COmparisonType
                throw new NotImplementedException();
            }

            Expression VisitBooleanIsNullExpression(BooleanIsNullExpression isNull)
            {
                throw new NotImplementedException();
            }

            Expression VisitBooleanNotExpression(BooleanNotExpression not)
            {
                throw new NotImplementedException();
            }

            Expression VisitBooleanParenthesisExpression(BooleanParenthesisExpression parenthesis)
            {
                throw new NotImplementedException();
            }

            Expression VisitInPredicate(InPredicate inPredicate)
            {
                throw new NotImplementedException();
            }

            Expression VisitScalarExpression(ScalarExpression scalarExpression)
            {
                switch (scalarExpression)
                {
                    case FunctionCall functionCall:
                        return VisitFunctionCall(functionCall);
                    case Literal literal:
                        return new Expression { Literal = VisitLiteral(literal) };
                    case ColumnReferenceExpression columnReferenceExpression:

                    default:
                        throw new NotSupportedException(scalarExpression.GetType().Name);
                }
            }

            Expression VisitFunctionCall(FunctionCall functionCall)
            {
                uint functionReference = LookupFunction(functionCall.FunctionName);
                var result = new Expression { ScalarFunction = new Expression.Types.ScalarFunction { FunctionReference = functionReference, OutputType = I32 } };
                result.ScalarFunction.Arguments.AddRange(functionCall.Parameters.Select(a => new FunctionArgument { Value = VisitScalarExpression(a) }));
                return result;
            }

            Expression.Types.Literal VisitLiteral(Literal literal)
            {
                switch (literal)
                {
                    case IntegerLiteral integerLiteral:
                        return new Expression.Types.Literal { I32 = int.Parse(integerLiteral.Value) };
                    case StringLiteral stringLiteral:
                        return new Expression.Types.Literal { String = stringLiteral.Value };
                    default:
                        throw new NotSupportedException(literal.GetType().Name);
                }
            }

            static void NotSupported(object value, string name)
            {
                if (value != null) { throw new NotSupportedException(name); }
            }

            uint LookupFunction(Identifier identifier)
            {
                return 1;
            }
        }
    }
}
