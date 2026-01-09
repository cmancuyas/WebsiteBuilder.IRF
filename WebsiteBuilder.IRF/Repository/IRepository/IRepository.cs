using System.Linq.Expressions;

namespace WebsiteBuilder.IRF.Repository.IRepository
{
    public interface IRepository<T> where T : class
    {
        // Query builders
        IQueryable<T> Query(bool tracked = false);

        IQueryable<T> Query(
            Expression<Func<T, bool>> filter,
            bool tracked = false);

        // Get single
        Task<T?> GetAsync(
            Expression<Func<T, bool>> filter,
            bool tracked = false,
            CancellationToken cancellationToken = default,
            params Expression<Func<T, object>>[] includes);

        // Get many
        Task<IReadOnlyList<T>> GetAllAsync(
            Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            int pageSize = 0,
            int pageNumber = 1,
            bool tracked = false,
            CancellationToken cancellationToken = default,
            params Expression<Func<T, object>>[] includes);

        // Existence / counts
        Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default);

        Task<int> CountAsync(
            Expression<Func<T, bool>>? predicate = null,
            CancellationToken cancellationToken = default);

        // Commands (NO SaveChanges here)
        Task AddAsync(T entity, CancellationToken cancellationToken = default);
        Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        void Update(T entity);
        void UpdateRange(IEnumerable<T> entities);

        void Remove(T entity);
        void RemoveRange(IEnumerable<T> entities);
    }
}
