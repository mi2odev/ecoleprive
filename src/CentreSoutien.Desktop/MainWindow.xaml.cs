using System.Windows;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(Navigator navigator)
    {
        InitializeComponent();
        // Each new page starts at the top.
        navigator.Navigated += (_, _) => PageScroller.ScrollToTop();
    }
}
