using Microsoft.Extensions.Options;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaCleanupOptions
{
    public bool Enabled { get; set; } = true;
    public int RunHourLocal { get; set; } = 2;     // Asia/Manila hour
    public int RetentionDays { get; set; } = 7;
    public int BatchSize { get; set; } = 200;
}

public sealed class MediaCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaCleanupOptions _opts;
    private readonly TimeZoneInfo _tz;

    public MediaCleanupHostedService(IServiceScopeFactory scopeFactory, IOptions<MediaCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _opts = options.Value;

        // Uses Windows/IANA compatible ID on Windows; your service earlier used this.
        _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Safety floor: never spin tightly
        var minDelay = TimeSpan.FromMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_opts.Enabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                var delay = GetDelayUntilNextRunLocal(_opts.RunHourLocal);
                if (delay < minDelay) delay = minDelay;

                await Task.Delay(delay, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IMediaCleanupRunner>();

                // Run one batch
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
                break;
            }
            catch
            {
                // best-effort: do not crash the process
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private TimeSpan GetDelayUntilNextRunLocal(int runHourLocal)
    {
        if (runHourLocal < 0 || runHourLocal > 23)
            runHourLocal = 2;

        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _tz);

        var nextLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, runHourLocal, 0, 0);
        if (nowLocal >= nextLocal)
            nextLocal = nextLocal.AddDays(1);

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, _tz);
        return nextUtc - nowUtc;
    }
}
