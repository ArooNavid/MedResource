using AtomicNotes.Core.Models;
using AtomicNotes.Tests.Support;
using AtomicNotes.WPF.ViewModels;

namespace AtomicNotes.Tests;

public sealed class ActivityAuthorizationTests
{
    [Fact]
    public async Task Regular_user_sees_only_own_stats_and_admin_can_compare_everyone()
    {
        using var database = new ActivityDatabase();
        var regularId = await database.Users.CreateAsync(User("sara", UserRole.User));
        var adminId = await database.Users.CreateAsync(User("navid", UserRole.Admin));

        await database.Stats.IncrementDailyCountAsync(regularId, DailyCountType.PdfImport);
        await database.Stats.IncrementDailyCountAsync(regularId, DailyCountType.NoteCreate);
        await database.Stats.IncrementDailyCountAsync(regularId, DailyCountType.NoteCreate);
        await database.Stats.RecordLoginAsync(regularId);

        await database.Stats.IncrementDailyCountAsync(adminId, DailyCountType.PdfImport);
        await database.Stats.IncrementDailyCountAsync(adminId, DailyCountType.PdfImport);
        await database.Stats.IncrementDailyCountAsync(adminId, DailyCountType.PdfImport);
        await database.Stats.IncrementDailyCountAsync(adminId, DailyCountType.NoteCreate);

        var own = await database.Stats.GetReportAsync(
            regularId, regularId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal("sara", own.Username);
        Assert.Equal(1, own.TotalPdfImports);
        Assert.Equal(2, own.TotalNotesCreated);
        Assert.Equal(1, own.TotalSessions);

        var today = await database.Stats.GetTodayStatsAsync(regularId);
        Assert.Equal(regularId, today.UserId);
        Assert.Equal(1, today.PdfImportCount);
        Assert.Equal(2, today.NoteCreateCount);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.Stats.GetReportAsync(
            regularId, adminId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.Stats.GetSessionsAsync(
            regularId, adminId, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.Stats.GetUserComparisonAsync(
            regularId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));

        var adminView = await database.Stats.GetReportAsync(
            adminId, regularId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(2, adminView.TotalNotesCreated);

        var comparison = await database.Stats.GetUserComparisonAsync(
            adminId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(2, comparison.Count);
        Assert.Equal("navid", comparison[0].Username);
        Assert.Equal(3, comparison[0].TotalPdfImports);
        Assert.Equal(UserRole.Admin, comparison[0].Role);
        Assert.Equal("sara", comparison[1].Username);
        Assert.Equal(2, comparison[1].TotalNotesCreated);
        Assert.Equal(UserRole.User, comparison[1].Role);

        var regularDashboard = new DashboardViewModel(database.Users, database.Stats, regularId);
        await regularDashboard.LoadAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.False(regularDashboard.IsAdmin);
        Assert.Empty(regularDashboard.Comparison);
        Assert.Equal(1, regularDashboard.OwnToday!.PdfImportCount);

        var adminDashboard = new DashboardViewModel(database.Users, database.Stats, adminId);
        await adminDashboard.LoadAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.True(adminDashboard.IsAdmin);
        Assert.Equal(2, adminDashboard.Comparison.Count);
        Assert.Equal(new[] { "navid", "sara" }, adminDashboard.Comparison.Select(row => row.Username).ToArray());
    }

    private static User User(string name, UserRole role) => new()
    {
        Username = name,
        DisplayName = name,
        PasswordHash = "hash",
        Salt = "salt",
        Role = role
    };
}
