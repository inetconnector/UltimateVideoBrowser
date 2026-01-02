namespace UltimateVideoBrowser.Models;

[Flags]
public enum MediaType
{
    None = 0,
    Videos = 1,
    Photos = 2,
    Documents = 4,
    Graphics = 8,
    All = Videos | Photos | Documents | Graphics
}
