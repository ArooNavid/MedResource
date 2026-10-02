namespace AtomicNotes.Core.Models;

/// <summary>
/// A regular user sees only their own activity. An admin can read every user and compare them.
/// </summary>
public enum UserRole
{
    User = 0,
    Admin = 1
}
