namespace WebApiQueryBuilder
{
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