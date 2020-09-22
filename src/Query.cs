using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace WebApiQueryBuilder
{
    public class Query
    {
        public uint? Skip { get; set; }
        public uint? Take { get; set; }
        public string Filter { get; set; }
        public string Sort { get; set; }
    }

    public class QueryModelBinder : IModelBinder
    {
        public bool BindModel(HttpActionContext pActionContext, ModelBindingContext pBindingContext)
        {
            if (pBindingContext.ModelType == null)
            {
                return false;
            }

            NameValueCollection xResult = HttpUtility.ParseQueryString(pActionContext.Request.RequestUri.Query);

            var xQuery = new Query();

            foreach (var xKey in xResult.AllKeys.Select(pString => pString.Trim().ToLower()))
            {
                switch (xKey)
                {
                    case "skip":
                        xQuery.Skip = Convert.ToUInt32(xResult.Get(xKey));
                        break;
                    case "top":
                        xQuery.Take = Convert.ToUInt32(xResult.Get(xKey));
                        break;
                    case "sort":
                        xQuery.Sort = xResult.Get(xKey);
                        break;
                    case "filter":
                        xQuery.Filter = xResult.Get(xKey);
                        break;
                }
            }

            pBindingContext.Model = xQuery;

            return true;
        }
    }

    public static class QueryableExtensions
    {
        public static IQueryable<T> ApplyQuery<T>(this IQueryable<T> pParentQuery, Query pQuery)
        {
            if (pQuery == null)
            {
                return pParentQuery;
            }

            return pParentQuery
                .ApplyFilter(pQuery.Filter)
                .ApplyOrder(pQuery.Sort)
                .ApplySkip(pQuery.Skip, pQuery.Take);
        }

        private static IQueryable<T> ApplyOrder<T>(this IQueryable<T> query, string order)
        {
            if (String.IsNullOrEmpty(order))
            {
                return query;
            }

            try
            {
                var xCompiledOrdering = OrderByParser.Parse(order);
                return xCompiledOrdering.Apply(query);
            }
            catch (Exception xE)
            {
                throw new FormatException($"Provided sort expression '{order}' has incorrect format", xE);
            }
        }

        private static IQueryable<T> ApplyFilter<T>(this IQueryable<T> pQuery, string pFilter)
        {
            if (String.IsNullOrEmpty(pFilter))
            {
                return pQuery;
            }
            try
            {
                var xCompiledFilter = new ODataFilterLanguage().Parse<T>(pFilter);
                return pQuery.Where(xCompiledFilter);
            }
            catch (Exception e)
            {
                throw new FormatException($"Provided filter expression '{pFilter}' has incorrect format", e);
            }
        }

        private static IQueryable<T> ApplySkip<T>(this IQueryable<T> pQuery, uint? pSkip, uint? pTake)
        {
            return pQuery
                .SkipIf(pSkip.HasValue, (int) pSkip.GetValueOrDefault())
                .TakeIf(pTake.HasValue, (int) pTake.GetValueOrDefault());
        }

        private static IQueryable<T> SkipIf<T>(this IQueryable<T> pQuery, bool pPredicate, int pSkip)
        {
            return pPredicate ? pQuery.Skip(pSkip) : pQuery;
        }

        private static IQueryable<T> TakeIf<T>(this IQueryable<T> pQuery, bool pPredicate, int pSkip)
        {
            return pPredicate ? pQuery.Take(pSkip) : pQuery;
        }
    }

    public static class OrderByParser
    {
        public static OrderToken Parse(string pOrderBy)
        {
            if (String.IsNullOrEmpty(pOrderBy)) throw new ArgumentNullException(nameof(pOrderBy));

            var xTokens = pOrderBy.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
                .Select(pSegment => new OrderToken(pSegment)).ToArray();

            var xOrderToken = xTokens[0];
            xTokens.Skip(1).Aggregate(xOrderToken, (pToken, pNext) => pToken.Next = pNext);

            return xOrderToken;
        }
    }

    public class OrderToken
    {
        private readonly string PropertyPath;
        private readonly SortOrder Order;
        public OrderToken Next { get; set; }

        public OrderToken(string pSegment)
        {
            var xParts = pSegment.Trim().Split(' ').Select(x => x.Trim()).ToArray();
            if (xParts.Length < 1 || xParts.Length > 2)
                throw new ArgumentException($"Segment '{pSegment}' has incorrect format");

            PropertyPath = xParts[0];
            Order = xParts.Length == 2 ? (SortOrder) Enum.Parse(typeof(SortOrder), CultureInfo.InvariantCulture.TextInfo.ToTitleCase(xParts[1])) : SortOrder.Asc;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> pQuery, bool pFirstCall = true)
        {
            var xParameter = Expression.Parameter(typeof(T), "x");
            var xMemberExpression = (MemberExpression) PropertyPath.Split('.').Aggregate((Expression) xParameter, Expression.Property);

            var xCall = Expression.Call(
                typeof(Queryable), ChooseMethod(), new[] {
                    typeof(T),
                    ((PropertyInfo) xMemberExpression.Member).PropertyType
                },
                pQuery.Expression,
                Expression.Lambda(xMemberExpression, xParameter));

            var xOrdered = (IOrderedQueryable<T>) pQuery.Provider.CreateQuery<T>(xCall);

            var xRetorno = Next?.Apply(xOrdered, false)
                           ?? xOrdered;
            return xRetorno;

            string ChooseMethod()
            {
                switch (Order)
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