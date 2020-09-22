using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace WebApiQueryBuilder
{
    public class OrderToken
    {
        private readonly string _propertyPath;
        private readonly SortOrder _order;
        private OrderToken Next { get; }

        public OrderToken(string pSegment, OrderToken next)
        {
            Next = next;
            var xParts = pSegment.Trim().Split(' ').Select(x => x.Trim()).ToArray();
            if (xParts.Length < 1 || xParts.Length > 2)
                throw new ArgumentException($"Segment '{pSegment}' has incorrect format");

            _propertyPath = xParts[0];
            _order = xParts.Length == 2 ? (SortOrder) Enum.Parse(typeof(SortOrder), CultureInfo.InvariantCulture.TextInfo.ToTitleCase(xParts[1])) : SortOrder.Asc;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> pQuery, bool pFirstCall = true)
        {
            var xParameter = Expression.Parameter(typeof(T), "x");
            var xMemberExpression = (MemberExpression) _propertyPath.Split('.').Aggregate((Expression) xParameter, Expression.Property);

            var xCall = Expression.Call(
                typeof(Queryable), ChooseMethod(), new[] {
                    typeof(T),
                    ((PropertyInfo) xMemberExpression.Member).PropertyType
                },
                pQuery.Expression,
                Expression.Lambda(xMemberExpression, xParameter));

            var xOrdered = (IOrderedQueryable<T>) pQuery.Provider.CreateQuery<T>(xCall);

            var xReturn = Next?.Apply(xOrdered, false)
                           ?? xOrdered;
            return xReturn;

            string ChooseMethod()
            {
                switch (_order)
                {
                    case SortOrder.Asc: return pFirstCall ? "OrderBy" : "ThenBy";
                    case SortOrder.Desc: return pFirstCall ? "OrderByDescending" : "ThenByDescending";
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private enum SortOrder
        {
            Asc,
            Desc
        }
    }
}