using System.Xml.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TagLib;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;
using File = System.IO.File;

namespace PersonalYtDumper;

public class YtDumper : BackgroundService
{
    private readonly ILogger<YtDumper> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private PeriodicTimer _timer;
    private HashSet<string> _downloads = new();
    private readonly IConfiguration _config;
    private ProgressStringer _progressStringer = new();
    
    public YtDumper(IConfiguration config, ILogger<YtDumper> logger, IHostApplicationLifetime hostApplicationLifetime)
    {
        _config = config;
        
        if (!string.IsNullOrWhiteSpace(config.DownloadListCachePath))
            LoadCache();
        
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _hostApplicationLifetime.ApplicationStarted.Register(() => _logger.LogInformation(
            "PersonalYtDumper application started at: {time}.",
            DateTimeOffset.Now));
        _hostApplicationLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation(
                "PersonalYtDumper application stopping at: {time}.",
                DateTimeOffset.Now);

            if (_downloads.Count > 0)
            {
                FlushCache();
            }
            
        });
        _hostApplicationLifetime.ApplicationStopped.Register(() => _logger.LogInformation(
            "PersonalYtDumper application stopped at: {time}.",
            DateTimeOffset.Now));
        _progressStringer.OnProgressChanged += LogProgress;
    }
    private void LoadCache()
    {
        if (!File.Exists(_config.DownloadListCachePath))
        {
            File.Create(_config.DownloadListCachePath);
            return;
        }

        _downloads = File.ReadAllLines(_config.DownloadListCachePath).ToHashSet();
    }

    private void FlushCache()
    {
        File.WriteAllLines(_config.DownloadListCachePath, _downloads);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _timer = new PeriodicTimer(_config.PoolingPeriod);

        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckPlaylists();
        }
    }
    
    //requirements:
    //periodically check the playlists
    //go through each video, grab the id and check if it was already downloaded
    //if not, then download.
    private async ValueTask CheckPlaylists()
    {
        var youtube = new YoutubeClient();
        var videos = await youtube.Playlists.GetVideosAsync("https://www.youtube.com/playlist?list=PLOcCWb98xDRUJTKYNZAT6vBmTzvEvBRcf");
        foreach (var video in videos)
        {
            if (_downloads.Contains(video.Id))
            {
                _logger.LogDebug("Video {video} already in cache. Skipping...", video.Title);
                continue;
            }
            
            try
            {
                var url = video.Url;
                var videoObj = await youtube.Videos.GetAsync (url);
                var rootFileName = MakeValidFileName(videoObj.Title.Trim()).Replace(" ", "_");

                #region Donwload Metadata

                var destinationMetadataPath = $"{_config.DownloadPath}/{rootFileName}.xml";

                if (!Directory.Exists(_config.DownloadPath))
                {
                    Directory.CreateDirectory(_config.DownloadPath);
                }
                
                var fs = new FileStream (destinationMetadataPath, FileMode.Create);
                var sw = new StreamWriter (fs); 
                var dto = await GetVideoDto (videoObj);
                var serializer = new XmlSerializer (typeof (VideoDto));
                serializer.Serialize (sw, dto);

                #endregion

                #region Download Thumb
                
                var thumb = video.Thumbnails.MaxBy (x => x.Resolution.Area);
                var thumbPath = destinationMetadataPath = $"{_config.DownloadPath}/{rootFileName}.jpg";
                
                if (thumb.Url.Contains("jpg"))
                {
                    var httpClient = new HttpClient();
                    var resp = await httpClient.GetAsync (thumb.Url);
                    var urlStream = await resp.Content.ReadAsStreamAsync();
                    await using var thumbFile = new FileStream(thumbPath, FileMode.Create);
                    await urlStream.CopyToAsync(thumbFile);
                }
                

                #endregion
                
                #region Download File

                var destinationDownloadPath = $"{_config.DownloadPath}/{rootFileName}.mp3";
                if (!File.Exists(destinationDownloadPath))
                {
                    _logger.LogInformation("Downloading {file}: start", rootFileName);
                    _logger.LogInformation($"Download Status: {string.Join("", Enumerable.Range(1, 10).Select(x=>"."))}");
                    await youtube.Videos.DownloadAsync(url, destinationDownloadPath, _progressStringer);
                    _logger.LogInformation("Downloading {file}: finish", rootFileName);
                    _downloads.Add(video.Id);
                }

                var tagFile = TagLib.File.Create(destinationDownloadPath);
                var pic = new Picture(thumbPath);
                tagFile.Tag.Pictures = new IPicture[1] { pic };
                tagFile.Save();
                
                #endregion
                FlushCache();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error: {ex}", ex);
            }
        }
    }

    private readonly object _locker = new();
    private string _lastReturn;
    private void LogProgress(string str)
    {
        lock (_locker)
        {
            if (_lastReturn == str) return;
            _logger.LogInformation("Download Status: {status}", str);
            _lastReturn = str;
        }
    }
    
    private class ProgressStringer : Progress<double>
    {
        private int _latestInt = 0;
        public event Action<string>? OnProgressChanged;
        
        public ProgressStringer() : base()
        {
            base.ProgressChanged += ((sender, d) =>
            {
                var intPart = (int)Math.Round(d * 100, 0);
                if (intPart == _latestInt/10) return;
                _latestInt = intPart;

                var progressString = string.Join("", Enumerable.Range(1, 10).Select(x => x <= _latestInt ? "=" : "."));
                
                OnProgressChanged?.Invoke(progressString);
            });
        }
    }
    
    private static async Task<VideoDto> GetVideoDto(YoutubeExplode.Videos.Video video) 
    {
        var dto = new VideoDto();
        dto.Author = video.Author.ChannelTitle;
        dto.Title = video.Title;
        dto.Duration = video.Duration;
        dto.Description = video.Description;
        dto.UploadDate = video.UploadDate;
        try
        {
            var thumb = video.Thumbnails.MaxBy(x => x.Resolution.Area);

            if (thumb is not null)
            {
                var httpClient = new HttpClient();
                var resp = await httpClient.GetAsync (thumb.Url);
                var urlStream = await resp.Content.ReadAsStreamAsync();
                using var memoryStream = new MemoryStream();
                await urlStream.CopyToAsync (memoryStream);
                dto.Thumbnail = Convert.ToBase64String (memoryStream.ToArray());
            }
        }
        catch (Exception ex) 
        {
		
        }

        return dto;
    }

    private static string MakeValidFileName(string name)
    {
        string invalidChars = System.Text.RegularExpressions.Regex.Escape( new string( System.IO.Path.GetInvalidFileNameChars() ) );
        string invalidRegStr = string.Format( @"([{0}]*\.+$)|([{0}]+)", invalidChars );

        return System.Text.RegularExpressions.Regex.Replace( name, invalidRegStr, "_" );
    }
}

