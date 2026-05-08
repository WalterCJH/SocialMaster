using Newtonsoft.Json;

namespace SocialMaster.Sources.Douyin;

// 對應 API_Response/Douyin/{secUserId}/*.json 的檔案結構（整頁回應）
public class ApiResponseWrapper
{
    [JsonProperty("fetched_at")]
    public string FetchedAt { get; set; } = "";

    [JsonProperty("api_url")]
    public string ApiUrl { get; set; } = "";

    [JsonProperty("data")]
    public DouyinApiResponse? Data { get; set; }
}

// 對應 accounts/{id}/json/{aweme_id}.json 的檔案結構（單筆影片）
public class AwemeFileWrapper
{
    [JsonProperty("fetched_at")]
    public string FetchedAt { get; set; } = "";

    [JsonProperty("api_url")]
    public string ApiUrl { get; set; } = "";

    [JsonProperty("data")]
    public DouyinAweme? Data { get; set; }
}

public class DouyinApiResponse
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("data")]
    public DouyinData? Data { get; set; }
}

public class DouyinData
{
    [JsonProperty("aweme_list")]
    public List<DouyinAweme> AwemeList { get; set; } = new();

    [JsonProperty("has_more")]
    public int HasMore { get; set; }

    [JsonProperty("max_cursor")]
    public long MaxCursor { get; set; }
}

public class DouyinAweme
{
    [JsonProperty("aweme_id")]
    public string AwemeId { get; set; } = "";

    [JsonProperty("desc")]
    public string Desc { get; set; } = "";

    [JsonProperty("media_type")]
    public int MediaType { get; set; }

    [JsonProperty("video")]
    public DouyinVideo? Video { get; set; }
}

public class DouyinVideo
{
    [JsonProperty("width")]
    public int Width { get; set; }

    [JsonProperty("height")]
    public int Height { get; set; }

    [JsonProperty("play_addr_h264")]
    public DouyinUrlList? PlayAddrH264 { get; set; }

    [JsonProperty("play_addr")]
    public DouyinUrlList? PlayAddr { get; set; }

    [JsonProperty("play_addr_265")]
    public DouyinUrlList? PlayAddr265 { get; set; }

    [JsonProperty("download_addr")]
    public DouyinUrlList? DownloadAddr { get; set; }
}

public class DouyinUrlList
{
    [JsonProperty("url_list")]
    public List<string> UrlList { get; set; } = new();
}
