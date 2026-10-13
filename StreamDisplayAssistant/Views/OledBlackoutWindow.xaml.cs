using System.Windows;
using System.Windows.Input;

namespace StreamDisplayAssistant.Views;

public partial class OledBlackoutWindow : Window
{
    public OledBlackoutWindow()
    {
        InitializeComponent();
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Normal;
        
        // Multi-Monitor Virtual Screen Bounding Box (blankets all connected displays in 100% #000000 pure black)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Center the HUD on the Primary Screen for optimal readability
        if (HudBorder != null)
        {
            double primaryWidth = SystemParameters.PrimaryScreenWidth;
            double primaryHeight = SystemParameters.PrimaryScreenHeight;
            double offsetX = -SystemParameters.VirtualScreenLeft;
            double offsetY = -SystemParameters.VirtualScreenTop;

            HudBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            HudBorder.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            HudBorder.Margin = new Thickness(
                offsetX + (primaryWidth - 460) / 2.0,
                offsetY + (primaryHeight - 220) / 2.0,
                0,
                0);
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Space || e.Key == Key.Enter)
        {
            Close();
        }
    }

    private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Double-click anywhere to exit quickly with mouse
        if (e.ClickCount >= 2)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
