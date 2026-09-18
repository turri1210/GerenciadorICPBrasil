using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ConfigAuditoria.Logging;
using ConfigAuditoria.Models;
using ConfigAuditoria.Services;
using ConfigAuditoria.ViewModels;

namespace ConfigAuditoria;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var auditService = new ConfigurationAuditService();
        _viewModel = new MainViewModel(auditService);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        foreach (var category in _viewModel.Categories)
        {
            category.PropertyChanged += OnCategoryPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            UpdateCursor();
        }
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigurationCategory.IsLoading))
        {
            UpdateCursor();
        }
    }

    private void UpdateCursor()
    {
        if (_viewModel.Categories.Any(c => c.IsLoading))
        {
            Mouse.OverrideCursor = Cursors.AppStarting;
        }
        else
        {
            Mouse.OverrideCursor = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Mouse.OverrideCursor = null;
    }

    private void OnSaveEvidenceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var (reportPath, screenshotPath, fallbackUsed) = ResolveEvidencePaths(timestamp);

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);

            SaveWindowScreenshot(this, screenshotPath);
            var reportEntry = _viewModel.BuildEvidenceReport(screenshotPath);
            File.AppendAllText(reportPath, reportEntry, Encoding.UTF8);

            var message = fallbackUsed
                ? $"Não foi possivel utilizar o local escolhido. As evidências foram salvas automaticamente na Area de Trabalho:\n{reportPath}\n{screenshotPath}"
                : $"Evidências salvas em:\n{reportPath}\n{screenshotPath}";

            MessageBox.Show(
                message,
                "ConfigAuditoria",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao salvar as evidências.");
            MessageBox.Show(
                $"Nao foi possivel salvar as evidências: {ex.Message}",
                "ConfigAuditoria",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void SaveWindowScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)(window.ActualHeight * dpi.DpiScaleY));

        var renderTarget = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        renderTarget.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private (string ReportPath, string ScreenshotPath, bool UsedFallback) ResolveEvidencePaths(string timestamp)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar evidências",
                Filter = "Arquivos de texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
                FileName = $"Evidencias_{timestamp}.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            var result = dialog.ShowDialog(this);
            if (result == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                var directory = Path.GetDirectoryName(dialog.FileName)!;
                var screenshotPath = Path.Combine(directory, $"Evidencia_{timestamp}.png");
                return (dialog.FileName, screenshotPath, false);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao selecionar local para salvar evidências. Aplicando fallback para escritorio.");
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var fallbackReport = Path.Combine(desktop, "Evidências.txt");
        var fallbackScreenshot = Path.Combine(desktop, $"Evidencia_{timestamp}.png");
        return (fallbackReport, fallbackScreenshot, true);
    }
}
