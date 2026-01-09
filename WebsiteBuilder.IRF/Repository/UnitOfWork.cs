using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Repository.IRepository;

namespace WebsiteBuilder.IRF.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly DataContext _db;
        private IDbContextTransaction _transaction;

        //public IEducationalAttainmentRepository EducationalAttainment { get; private set; }

        public UnitOfWork(DataContext db)
        {
            _db = db;
            //EducationalAttainment = new EducationalAttainmentRepository(_db);
        }

        public async Task SaveAsync()
        {
            await _db.SaveChangesAsync();
        }

        public void Save()
        {
            _db.SaveChanges();
        }

        public async Task CompleteAsync()
        {
            await _db.SaveChangesAsync();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            _transaction = await _db.Database.BeginTransactionAsync();
            return _transaction;
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }
        public async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    await operation();

                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    T result = await operation();

                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
    }
}
