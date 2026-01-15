using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Repository.IRepository;

namespace WebsiteBuilder.IRF.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly DataContext _db;

        // FIX: nullable, because it is not initialized in ctor and may not exist.
        private IDbContextTransaction? _transaction;

        public UnitOfWork(DataContext db)
        {
            _db = db;
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _db.SaveChangesAsync(ct);
        }

        public void Save()
        {
            _db.SaveChanges();
        }

        public async Task CompleteAsync(CancellationToken ct = default)
        {
            await _db.SaveChangesAsync(ct);
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _db.Dispose();
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            // If caller begins twice, close previous transaction cleanly (optional policy).
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }

            _transaction = await _db.Database.BeginTransactionAsync(ct);
            return _transaction;
        }

        public async Task CommitTransactionAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                return; // or throw new InvalidOperationException("No active transaction.");

            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        public async Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                return; // or throw new InvalidOperationException("No active transaction.");

            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    await operation();

                    // If your operation already saves, this is harmless (but redundant).
                    // If you want strict control, remove this and require caller to SaveAsync.
                    await _db.SaveChangesAsync(ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await operation();

                    await _db.SaveChangesAsync(ct);

                    await tx.CommitAsync(ct);

                    return result;
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }
    }
}
