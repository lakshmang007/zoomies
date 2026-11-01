using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DemoRecorder;

/// <summary>
/// A stand-in note-taking app whose Save dialog (the standard Windows one every app uses) opens inside the sample PC.
/// Real apps such as Paint reopen their last folder, which on the recording PC was the user's own OneDrive.
/// </summary>
internal sealed class NotesWindow : Window
{
    public NotesWindow(string saveFolder)
    {
        Title = "Viva prep - Notes";
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        var menu = new TextBlock
        {
            Text = "File     Edit     View",
            Foreground = Brushes.Gainsboro, FontSize = 14, Margin = new Thickness(16, 10, 16, 10),
        };
        var text = new TextBox
        {
            Text = "Final year project — viva prep\n\n• Demo the app live\n• Explain how the search index stays small\n• Show undo for every action\n• Questions on privacy: everything stays on the PC\n",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 17, Padding = new Thickness(20, 14, 20, 14),
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x27)), Foreground = Brushes.WhiteSmoke,
            BorderThickness = new Thickness(0), CaretBrush = Brushes.White,
        };
        var root = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        root.Children.Add(text);
        Content = root;

        InputBindings.Add(new KeyBinding(new SaveCommand(this, saveFolder), Key.S, ModifierKeys.Control));
    }

    private sealed class SaveCommand(Window owner, string folder) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                InitialDirectory = folder,
                FileName = "Viva prep",
                Filter = "All files (*.*)|*.*",
            };
            dialog.ShowDialog(owner); // the demo cancels it; nothing is ever saved
        }
    }
}
