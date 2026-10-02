using System.Data;

namespace AtomicNotes.Core.Interfaces;

public interface IDbConnectionFactory
{
    string DatabasePath { get; }
    IDbConnection Create();
}
