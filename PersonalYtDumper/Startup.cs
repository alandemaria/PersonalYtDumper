using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalYtDumper;
using Serilog;

static IHostBuilder CreateHostBuilder(string[] args)
{
    var logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.WithThreadId()
        .WriteTo.File(path: $@"C:/temp/pyd.log", rollingInterval: RollingInterval.Infinite, outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.ffffff} ({ThreadId:000}) [{Level:u3}] {Message:lj} {NewLine}{Exception}")
        .CreateLogger();
    
    return Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(logger));
            services.AddHostedService<YtDumper>();
            services.AddSingleton<IConfiguration>(x=> new YtDumperConfig()
            {
                DownloadPath = @"c:\youtubeDumper",
                DownloadListCachePath = @"c:\youtubeDumper\downloads.cache",
                PoolingPeriod = TimeSpan.FromSeconds(5)
            });
        });
}

var host = CreateHostBuilder(args).Build();

await host.RunAsync();