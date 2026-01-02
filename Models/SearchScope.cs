namespace UltimateVideoBrowser.Models;

[Flags]
public enum SearchScope
{
    None = 0,
    Name = 1,
    People = 2,
    Albums = 4,
    All = Name | People | Albums
}
