using System.Collections.Generic;
using System.Linq;

namespace CSharpNet6_10.CSharp3.Linq
{
    public class QueryExpressions
    {
        // A query expression is rewritten into method calls: this one becomes
        // values.Where(...).OrderBy(...).Select(...).
        public static List<int> EvenSquaresAscending(IEnumerable<int> values)
        {
            IEnumerable<int> query = from value in values
                                     where value % 2 == 0
                                     orderby value
                                     select value * value;
            return query.ToList();
        }

        // let introduces a range variable computed once per element.
        public static List<string> WithComputedLength(IEnumerable<string> names)
        {
            IEnumerable<string> query = from name in names
                                        let length = name.Length
                                        where length > 2
                                        select name + ":" + length;
            return query.ToList();
        }

        // group ... by yields a sequence of groupings keyed by the expression.
        public static int GroupCount(IEnumerable<string> names)
        {
            IEnumerable<IGrouping<int, string>> query = from name in names
                                                        group name by name.Length;
            return query.Count();
        }

        // join correlates two sequences on a key.
        public static List<string> JoinOnLength(IEnumerable<int> ids, IEnumerable<string> names)
        {
            IEnumerable<string> query = from id in ids
                                        join name in names on id equals name.Length
                                        select id + "=" + name;
            return query.ToList();
        }
    }
}
