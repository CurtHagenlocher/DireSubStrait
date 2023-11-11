using Google.Protobuf;
using Substrait.Protobuf;
using System.IO;
using System.Linq;
using static Substrait.Protobuf.ReadRel.Types;
using SubstraitType = Substrait.Protobuf.Type;

namespace DireSubStrait
{
    internal class Program
    {
        static readonly SubstraitType I16 = new SubstraitType() { I16 = new SubstraitType.Types.I16() { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType I16N = new SubstraitType() { I16 = new SubstraitType.Types.I16() { Nullability = SubstraitType.Types.Nullability.Nullable } };
        static readonly SubstraitType I32 = new SubstraitType() { I32 = new SubstraitType.Types.I32() { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType I32N = new SubstraitType() { I32 = new SubstraitType.Types.I32() { Nullability = SubstraitType.Types.Nullability.Nullable } };
        static readonly SubstraitType I64 = new SubstraitType() { I64 = new SubstraitType.Types.I64() { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType I64N = new SubstraitType() { I64 = new SubstraitType.Types.I64() { Nullability = SubstraitType.Types.Nullability.Nullable } };
        static readonly SubstraitType String = new SubstraitType() { String = new SubstraitType.Types.String { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType StringN = new SubstraitType() { String = new SubstraitType.Types.String { Nullability = SubstraitType.Types.Nullability.Nullable } };

        static readonly SubstraitType StringList = new SubstraitType { List = new SubstraitType.Types.List { Type = String, Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType StringI32 = Struct(SubstraitType.Types.Nullability.Required, StringN, I32N);

        static readonly SubstraitType Bool = new SubstraitType { Bool = new SubstraitType.Types.Boolean { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType BoolN = new SubstraitType { Bool = new SubstraitType.Types.Boolean { Nullability = SubstraitType.Types.Nullability.Nullable } };

        static readonly SubstraitType Date = new SubstraitType { Date = new SubstraitType.Types.Date { Nullability = SubstraitType.Types.Nullability.Required } };
        static readonly SubstraitType DateN = new SubstraitType { Date = new SubstraitType.Types.Date { Nullability = SubstraitType.Types.Nullability.Nullable } };

        static SubstraitType Struct(SubstraitType.Types.Nullability nullability, params SubstraitType[] types)
        {
            var result = new SubstraitType.Types.Struct();
            result.Types_.AddRange(types);
            result.Nullability = nullability;
            return new SubstraitType { Struct = result };
        }

        static SubstraitType DecimalType(int precision, int scale, bool nullable)
        {
            return new SubstraitType { Decimal = new SubstraitType.Types.Decimal { Precision = precision, Scale = scale, Nullability = nullable ? SubstraitType.Types.Nullability.Nullable : SubstraitType.Types.Nullability.Required } };
        }

        static RelCommon Emit(params int[] mapping)
        {
            var emit = new RelCommon.Types.Emit();
            emit.OutputMapping.AddRange(mapping);
            return new RelCommon { Emit = emit };
        }

        static Expression RootStructFieldReference(int field)
        {
            return new Expression { Selection = new Expression.Types.FieldReference { DirectReference = new Expression.Types.ReferenceSegment { StructField = new Expression.Types.ReferenceSegment.Types.StructField { Field = field } } } };
        }

        static Expression Cast(SubstraitType type, Expression expr)
        {
            return new Expression { Cast = new Expression.Types.Cast { Type = type, Input = expr } };
        }

        static Expression Invoke(uint functionReference, SubstraitType outputType, params Expression[] arguments)
        {
            var result = new Expression { ScalarFunction = new Expression.Types.ScalarFunction { FunctionReference = functionReference, OutputType = outputType } };
            result.ScalarFunction.Arguments.AddRange(arguments.Select(a => new FunctionArgument { Value = a }));
            return result;
        }

        static Expression Literal(string s)
        {
            return new Expression { Literal = new Expression.Types.Literal { String = s } };
        }

        static ReadRel ReadTable(string name)
        {
            var result = new ReadRel { NamedTable = new NamedTable() };
            result.NamedTable.Names.Add(name);
            result.BaseSchema = new NamedStruct { Struct = new SubstraitType.Types.Struct { Nullability = SubstraitType.Types.Nullability.Required } };
            return result;
        }

        static void Main(string[] args)
        {
            var orders = ReadTable("orders");
            orders.BaseSchema.Names.AddRange(new[] { "product_id", "quantity", "order_date", "price" });
            orders.BaseSchema.Struct.Types_.AddRange(new SubstraitType[]
            {
                I64, I32, Date, DecimalType(10, 2, true)
            });


            var products = ReadTable("products");
            products.BaseSchema.Names.AddRange(new[] { "product_id", "categories", "details", "manufacturer", "year_created", "product_name" });
            products.BaseSchema.Struct.Types_.AddRange(new SubstraitType[]
            {
                I64, StringList, Struct(SubstraitType.Types.Nullability.Nullable, StringN, I32N), StringN
            });


            var filter = new FilterRel();
            filter.Input = new Rel { Read = products };
            filter.Condition = Invoke(2, Bool, Invoke(1, I64N, Literal("Computers"), RootStructFieldReference(1)));

            var join = new JoinRel
            {
                Left = new Rel { Read = orders },
                Right = new Rel { Filter = filter },
                Type = JoinRel.Types.JoinType.Inner,
                Expression = Invoke(3, BoolN, RootStructFieldReference(0), RootStructFieldReference(4)),
            };

            var aggregate = new AggregateRel
            {
                Input = new Rel { Join = join },
                Common = Emit(1, 0, 2),
            };

            var grouping = new AggregateRel.Types.Grouping();
            grouping.GroupingExpressions.Add(RootStructFieldReference(0));
            grouping.GroupingExpressions.Add(RootStructFieldReference(7));
            aggregate.Groupings.Add(grouping);

            var measure = new AggregateRel.Types.Measure
            {
                Measure_ = new AggregateFunction
                {
                    FunctionReference = 4,
                    OutputType = DecimalType(38, 2, nullable: true),
                }
            };
            measure.Measure_.Arguments.Add(new FunctionArgument { Value = Cast(DecimalType(10, 2, false), RootStructFieldReference(1)) });
            measure.Measure_.Arguments.Add(new FunctionArgument { Value = RootStructFieldReference(3) });

            var planRel = new PlanRel { Root = new RelRoot { Input = new Rel { Aggregate = aggregate } } };
            planRel.Root.Names.AddRange(new[] { "product_name", "product_id", "sales" });

            var plan = new Plan { Version = new Substrait.Protobuf.Version { MinorNumber = 20, Producer = "validator-test" } };
            plan.ExtensionUris.Add(new SimpleExtensionURI { ExtensionUriAnchor = 1, Uri = "https://github.com/substrait-io/substrait/blob/main/extensions/functions_set.yaml" });
            plan.ExtensionUris.Add(new SimpleExtensionURI { ExtensionUriAnchor = 2, Uri = "https://github.com/substrait-io/substrait/blob/main/extensions/functions_comparison.yaml" });
            plan.ExtensionUris.Add(new SimpleExtensionURI { ExtensionUriAnchor = 3, Uri = "https://github.com/substrait-io/substrait/blob/main/extensions/functions_arithmetic_decimal.yaml" });
            plan.Extensions.Add(new SimpleExtensionDeclaration { ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction { ExtensionUriReference = 1, FunctionAnchor = 1, Name = "index_in" } });
            plan.Extensions.Add(new SimpleExtensionDeclaration { ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction { ExtensionUriReference = 2, FunctionAnchor = 2, Name = "is_null" } });
            plan.Extensions.Add(new SimpleExtensionDeclaration { ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction { ExtensionUriReference = 2, FunctionAnchor = 3, Name = "equal" } });
            plan.Extensions.Add(new SimpleExtensionDeclaration { ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction { ExtensionUriReference = 3, FunctionAnchor = 4, Name = "sum" } });
            plan.Extensions.Add(new SimpleExtensionDeclaration { ExtensionFunction = new SimpleExtensionDeclaration.Types.ExtensionFunction { ExtensionUriReference = 3, FunctionAnchor = 5, Name = "multiply" } });
            plan.Relations.Add(planRel);

            using (var output = File.Create("d:/testdata/plan.dat"))
            {
                plan.WriteTo(output);
            }
        }
    }
}
