using System;
using System.Linq.Expressions;

namespace Net7_CSharp10.CSharp3.ExpressionTrees
{
    public class ExpressionTreeSamples
    {
        // The same lambda syntax builds a data structure rather than a delegate
        // when the target type is Expression<TDelegate>.
        public static Expression<Func<int, int>> DoubleExpression()
        {
            return value => value * 2;
        }

        // The tree can be taken apart at run time — this is what LINQ providers
        // do to translate a query into another language.
        public static ExpressionType RootNodeType()
        {
            Expression<Func<int, int>> expression = value => value * 2;
            return expression.Body.NodeType;
        }

        // Compiling the tree produces the delegate the lambda would have been.
        public static int CompileAndInvoke()
        {
            Expression<Func<int, int>> expression = value => value * 2;
            Func<int, int> compiled = expression.Compile();
            return compiled(21);
        }

        // A tree can equally be assembled by hand from its node factories.
        public static int BuiltByHand()
        {
            ParameterExpression parameter = Expression.Parameter(typeof(int), "value");
            BinaryExpression body = Expression.Multiply(parameter, Expression.Constant(3));
            Expression<Func<int, int>> expression =
                Expression.Lambda<Func<int, int>>(body, parameter);
            return expression.Compile()(14);
        }
    }
}
