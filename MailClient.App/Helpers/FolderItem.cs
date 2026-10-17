using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace MailClient.App.Helpers;

public sealed class FolderItem(string tag, string name, string icon) : ObservableObject
{
    public string Tag { get; } = tag;
    public string Name { get; } = name;
    /// <summary>Svg icon kind.</summary>
    public string Icon { get; } = icon;

    private int _count;
    /// <summary>Unread (or, for drafts, total) message count shown as badge.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(PillVisibility));
                OnPropertyChanged(nameof(DotVisibility));
            }
        }
    }

    private bool _isCompact;
    /// <summary>True while the sidebar is collapsed to icons only.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (SetProperty(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(LabelVisibility));
                OnPropertyChanged(nameof(PillVisibility));
                OnPropertyChanged(nameof(DotVisibility));
            }
        }
    }

    private bool _isAvailable = true;
    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (SetProperty(ref _isAvailable, value))
                OnPropertyChanged(nameof(ItemOpacity));
        }
    }

    public string CountText => _count > 999 ? "999+" : _count.ToString();
    public Visibility LabelVisibility => _isCompact ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PillVisibility => _count > 0 && !_isCompact ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DotVisibility => _count > 0 && _isCompact ? Visibility.Visible : Visibility.Collapsed;
    public double ItemOpacity => _isAvailable ? 1.0 : 0.4;
}
