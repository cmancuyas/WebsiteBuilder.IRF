using Microsoft.EntityFrameworkCore.Storage;

namespace WebsiteBuilder.IRF.Repository.IRepository
{
    public interface IUnitOfWork
    {
        Task SaveAsync();
        void Save();
        Task CompleteAsync();
        void Dispose();
        Task ExecuteInTransactionAsync(Func<Task> operation);
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation);
        Task<IDbContextTransaction> BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
