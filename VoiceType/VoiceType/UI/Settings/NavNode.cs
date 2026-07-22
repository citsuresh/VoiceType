using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace VoiceType.UI.Settings
{
    /// <summary>
    /// A node in the Settings window's hierarchical navigation tree. A node with a non-null
    /// <see cref="Section"/> is selectable and hosts an <see cref="ISettingsSection"/>; a node
    /// with a null <see cref="Section"/> is a non-selectable parent category that only groups
    /// <see cref="Children"/> (reserved for future nested settings pages).
    /// </summary>
    public sealed class NavNode : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isExpanded = true;

        public NavNode(string title, UserControl? section = null)
        {
            Title = title;
            Section = section;
        }

        public string Title { get; }

        public UserControl? Section { get; }

        public List<NavNode> Children { get; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
