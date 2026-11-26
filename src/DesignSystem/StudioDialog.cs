using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Studio.DesignSystem
{
    /// <summary>
    /// The design system's pop-up: a floating layered card with pill buttons, used instead of the plain Windows message box
    /// so every question looks like the rest of the app. Same arguments and result as MessageBox.Show.
    /// </summary>
    public static class StudioDialog
    {
        public static MessageBoxResult Show(Window owner, string text, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        {
            var w = new Window
            {
                Owner = owner, Title = title, Width = 500, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = U.R("Bg"), ShowInTaskbar = false, Icon = owner?.Icon,
            };
            StudioLook.Window(w, ((SolidColorBrush)U.R("Bg")).Color);
            var result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
            var icon = image switch { MessageBoxImage.Warning => "safety", MessageBoxImage.Error => "safety", MessageBoxImage.Question => "helper", _ => "helper" };
            var body = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
            var head = U.Row(Icons.Get(icon, 40), U.T(title, 19, "TextStrong", FontWeights.Bold, true, new Thickness(14, 6, 0, 0)));
            head.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(head);
            body.Children.Add(U.T(text, 14.5, "Text", null, true, new Thickness(0, 0, 0, 20)));
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            void Add(string label, MessageBoxResult r, bool primary, bool cancel = false)
            {
                var b = new Button { Content = label, Style = U.S(primary ? "BtnPrimary" : "Pill.Quiet"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(20, 9, 20, 9), IsDefault = primary, IsCancel = cancel };
                b.Click += (s, e) => { result = r; w.DialogResult = true; };
                row.Children.Add(b);
            }
            switch (buttons)
            {
                case MessageBoxButton.OK: Add("OK", MessageBoxResult.OK, true); break;
                case MessageBoxButton.OKCancel: Add("Cancel", MessageBoxResult.Cancel, false, true); Add("OK", MessageBoxResult.OK, true); break;
                case MessageBoxButton.YesNo: Add("No", MessageBoxResult.No, false, true); Add("Yes", MessageBoxResult.Yes, true); break;
                case MessageBoxButton.YesNoCancel: Add("Cancel", MessageBoxResult.Cancel, false, true); Add("No", MessageBoxResult.No, false); Add("Yes", MessageBoxResult.Yes, true); break;
            }
            body.Children.Add(row);
            var card = U.Float(body, new Thickness(12), "Layer5", 16);
            w.Content = new Grid { Children = { new AuroraBackdrop(Glow.MaroonColor, Glow.AmberColor, Color.FromRgb(0x6B, 0x2A, 0x5C)) { Opacity = 0.6 }, card } };   // design-check: data (aurora tint)
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { w.DialogResult = false; } };
            w.ShowDialog();
            return result;
        }

        public static MessageBoxResult Show(string text, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
            => Show(Application.Current?.MainWindow, text, title, buttons, image);
    }
}
