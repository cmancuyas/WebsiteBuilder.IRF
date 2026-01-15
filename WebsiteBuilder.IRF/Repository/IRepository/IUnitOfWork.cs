using Microsoft.EntityFrameworkCore.Storage;

namespace WebsiteBuilder.IRF.Repository.IRepository
{
    public interface IUnitOfWork : IDisposable
    {
        Task SaveAsync(CancellationToken ct = default);
        void Save();
        Task CompleteAsync(CancellationToken ct = default);

        Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default);
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct = default);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
        Task CommitTransactionAsync(CancellationToken ct = default);
        Task RollbackTransactionAsync(CancellationToken ct = default);
    }
}
