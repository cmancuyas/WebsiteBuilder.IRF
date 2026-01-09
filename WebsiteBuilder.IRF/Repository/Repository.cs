using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Repository.IRepository;

namespace WebsiteBuilder.IRF.Repository
{
    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly DataContext _context;
        private readonly DbSet<T> _dbSet;

        public Repository(DataContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = _context.Set<T>();
        }

        // -----------------------------
        // Query builders
        // -----------------------------
        public IQueryable<T> Query(bool tracked = false)
            => tracked ? _dbSet : _dbSet.AsNoTracking();

        public IQueryable<T> Query(Expression<Func<T, bool>> filter, bool tracked = false)
            => Query(tracked).Where(filter);

        // -----------------------------
        // Get single
        // -----------------------------
        public async Task<T?> GetAsync(
            Expression<Func<T, bool>> filter,
            bool tracked = false,
            CancellationToken cancellationToken = default,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = Query(tracked).Where(filter);

            if (includes != null && includes.Length > 0)
            {
                foreach (var include in includes)
                    query = query.Include(include);
            }

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        // -----------------------------
        // Get many
        // -----------------------------
        public async Task<IReadOnlyList<T>> GetAllAsync(
            Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            int pageSize = 0,
            int pageNumber = 1,
            bool tracked = false,
            CancellationToken cancellationToken = default,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = Query(tracked);

            if (filter != null)
                query = query.Where(filter);

            if (includes != null && includes.Length > 0)
            {
                foreach (var include in includes)
                    query = query.Include(include);
            }

            if (orderBy != null)
                query = orderBy(query);

            // Optional paging (safe guards)
            if (pageSize > 0)
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize > 200) pageSize = 200; // guardrail

                query = query.Skip(pageSize * (pageNumber - 1)).Take(pageSize);
            }

            return await query.ToListAsync(cancellationToken);
        }

        // -----------------------------
        // Existence / counts
        // -----------------------------
        public async Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return await _dbSet.AnyAsync(predicate, cancellationToken);
        }

        public async Task<int> CountAsync(
            Expression<Func<T, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            return predicate == null
                ? await _dbSet.CountAsync(cancellationToken)
                : await _dbSet.CountAsync(predicate, cancellationToken);
        }

        // -----------------------------
        // Commands (NO SaveChanges here)
        // -----------------------------
        public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _dbSet.AddAsync(entity, cancellationToken);
        }

        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            await _dbSet.AddRangeAsync(entities, cancellationToken);
        }

        public void Update(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _dbSet.Update(entity);
        }

        public void UpdateRange(IEnumerable<T> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            _dbSet.UpdateRange(entities);
        }

        public void Remove(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _dbSet.Remove(entity);
        }

        public void RemoveRange(IEnumerable<T> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            _dbSet.RemoveRange(entities);
        }
    }
}
