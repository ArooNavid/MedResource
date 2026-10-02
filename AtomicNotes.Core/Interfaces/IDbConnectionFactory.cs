using System.Data;

namespace AtomicNotes.Core.Interfaces;

public interface IDbConnectionFactory
{
    string ConnectionString { get; }
    IDbConnection Create();
}
