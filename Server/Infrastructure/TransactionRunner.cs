using System.Data;
using MySqlConnector;

namespace Server.Infrastructure;

public interface ITransactionRunner
{
    Task<T> RunAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> work);
}

public class TransactionRunner : ITransactionRunner
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<TransactionRunner> _log;

    public TransactionRunner(IDbConnectionFactory db, ILogger<TransactionRunner> log)
    {
        _db = db;
        _log = log;
    }

    // READ COMMITTED: lần đọc sau khi đã khóa khay luôn thấy dữ liệu mới nhất.
    // Gặp deadlock (mã 1213) thì thử lại đúng một lần. Ném exception => tự rollback.
    public async Task<T> RunAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var conn = _db.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var result = await work(conn, tx);
                await tx.CommitAsync();
                return result;
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock && attempt == 1)
            {
                _log.LogWarning("Deadlock, thử lại transaction một lần");
            }
        }
    }
}