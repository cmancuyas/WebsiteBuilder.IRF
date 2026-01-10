using System;
using System.Collections.Generic;
using System.Text;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.Models.Extensions
{
    public static class PageQueryExtensions
    {
        public static IQueryable<Page> Published(this IQueryable<Page> query)
        {
            return query.Where(p =>
                p.PageStatusId == PageStatusIds.Published &&
                p.IsActive &&
                !p.IsDeleted);
        }
    }
}
