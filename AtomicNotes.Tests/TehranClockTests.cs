using AtomicNotes.Services;

namespace AtomicNotes.Tests;

public sealed class TehranClockTests
{
    [Fact]
    public void Utc_2030_lands_on_the_next_Tehran_date()
    {
        using var clock = new TehranClockService();
        var utc = new DateTime(2024, 3, 21, 20, 30, 0, DateTimeKind.Utc);

        var tehran = clock.UtcToTehran(utc);

        Assert.Equal(new DateTime(2024, 3, 22, 0, 0, 0), tehran);
        Assert.Equal("2024-03-22", clock.FormatTehranDate(utc));

        var stillSameDay = new DateTime(2024, 3, 21, 20, 29, 0, DateTimeKind.Utc);
        Assert.Equal("2024-03-21", clock.FormatTehranDate(stillSameDay));
    }

    [Fact]
    public async Task RestartAsync_keeps_the_tick_loop_running()
    {
        using var clock = new TehranClockService(TimeSpan.FromMilliseconds(30));
        var ticks = 0;
        clock.Tick += (_, _) => Interlocked.Increment(ref ticks);

        clock.Start();
        await WaitUntil(() => Volatile.Read(ref ticks) >= 1);
        var afterStart = Volatile.Read(ref ticks);

        await clock.RestartAsync();
        await WaitUntil(() => Volatile.Read(ref ticks) > afterStart);
        await clock.StopAsync();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Clock did not tick before the timeout.");
    }
}
