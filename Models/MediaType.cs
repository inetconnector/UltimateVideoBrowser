namespace UltimateVideoBrowser.Models;

[Flags]
public enum MediaType
{
    None = 0,
    Videos = 1,
    Photos = 2,
    Documents = 4,
    All = Videos | Photos | Documents
}
