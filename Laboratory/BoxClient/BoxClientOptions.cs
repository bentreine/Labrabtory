namespace Laboratory;

public class BoxClientOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string EnterpriseId { get; set; } = string.Empty;

    public string? DeveloperToken { set; get; } = null;
    public string? RootFolderId { get; set; } = null;
}
