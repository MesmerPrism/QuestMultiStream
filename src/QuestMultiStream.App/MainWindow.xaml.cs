using System.ComponentModel;
using System.Windows;
using QuestMultiStream.App.ViewModels;

namespace QuestMultiStream.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = new MainWindowViewModel(Dispatcher);
        DataContext = _viewModel;
        DesktopAppLog.Info("Main window constructed.");

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        DesktopAppLog.Info("Main window loaded.");
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Closing -= OnClosing;
        DesktopAppLog.Info("Main window closing.");
        _viewModel.Shutdown();
    }
}
