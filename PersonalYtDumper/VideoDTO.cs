namespace PersonalYtDumper;

public record VideoDto
{
    public string Title { get; set; }
    public string Author { get;set;  }
    public DateTimeOffset UploadDate { get;set;  }
    public string Description { get;set;  }
    public TimeSpan? Duration { get;set;  }
    public string Thumbnail { get;set;  }
}