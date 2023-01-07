namespace PersonalYtDumper;

public interface IConfiguration
{
    public string DownloadListCachePath { get; set; }
    public string DownloadPath { get; set; }
    public TimeSpan PoolingPeriod { get; set; }
}

public class YtDumperConfig : IConfiguration
{
    public string DownloadListCachePath { get; set; }
    public string DownloadPath { get; set; }
    public TimeSpan PoolingPeriod { get; set; }
}