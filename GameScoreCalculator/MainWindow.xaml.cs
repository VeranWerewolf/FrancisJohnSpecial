using GameScoreCalculator.Helpers;
using GameScoreCalculator.Models;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace GameScoreCalculator;

public partial class MainWindow : Window
{
    private CancellationTokenSource _cts;

    public MainWindow()
    {
        InitializeComponent();
        MessageBus.Subscribe<OutputMessage>(HandleOutputMessage);
    }

    private void HandleOutputMessage(OutputMessage message)
    {
        AddOutput(message.Text, message.Color);
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(txtDaysIgnored.Text, out int daysIgnored) || daysIgnored < 0)
        {
            AddOutput("Invalid DaysIgnored value! Using default 7 seconds.", Colors.Orange);
            daysIgnored = 7;
        }

        if (!int.TryParse(txtReviewThreshold.Text, out int reviewThreshold) || reviewThreshold < 0)
        {
            AddOutput("Invalid ReviewThreshold value! Using default 100.", Colors.Orange);
            reviewThreshold = 100;
        }

        btnLaunch.IsEnabled = false;
        btnStop.IsEnabled = true;
        cbAddNew.IsEnabled = false;
        cbUpdateExisting.IsEnabled = false;
        cbUpdateExcludedByAppDetails.IsEnabled = false;
        cbUpdateExcludedByReviewThreshold.IsEnabled = false;
        cbCreateExport.IsEnabled = false;
        txtReviewThreshold.IsEnabled = false;
        txtDaysIgnored.IsEnabled = false;

        try
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var settings = new ProcessorSettings()
            {
                DaysIgnored = daysIgnored,
                AddNew = cbAddNew.IsChecked == true,
                ReviewThreshold = reviewThreshold,
                UpdateExisting = cbUpdateExisting.IsChecked == true,
                UpdateExcludedByAppDetails = cbUpdateExcludedByAppDetails.IsChecked == true,
                UpdateExcludedByReviewThreshold = cbUpdateExcludedByReviewThreshold.IsChecked == true,
                CreateExport = cbCreateExport.IsChecked == true,
            };

            await Processor.Start(settings, token);
        }
        catch (OperationCanceledException)
        {
            AddOutput("Execution canceled by user!", Colors.Orange);
        }
        finally
        {
            btnStop.IsEnabled = false;
            btnLaunch.IsEnabled = true;
            cbAddNew.IsEnabled = true;
            cbUpdateExisting.IsEnabled = true;
            cbUpdateExcludedByAppDetails.IsEnabled = true;
            cbUpdateExcludedByReviewThreshold.IsEnabled = true;
            cbCreateExport.IsEnabled = true;
            txtReviewThreshold.IsEnabled = true;
            txtDaysIgnored.IsEnabled = true;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void AddOutput(string text, Color color)
    {
        Dispatcher.Invoke(() =>
        {
            var paragraph = (Paragraph)rtbOutput.Document.Blocks.FirstBlock;
            var run = new Run(text + "\n")
            {
                Foreground = new SolidColorBrush(color)
            };

            paragraph.Inlines.Add(run);

            var scrollViewer = GetScrollViewer(rtbOutput);

            if (scrollViewer != null &&
                scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 1)
            {
                rtbOutput.ScrollToEnd();
            }

            if (paragraph.Inlines.Count > 5000)
            {
                var inlines = paragraph.Inlines.ToList();
                var newParagraph = new Paragraph();

                foreach (var inline in inlines.Skip(1000))
                {
                    newParagraph.Inlines.Add(inline);
                }

                rtbOutput.Document.Blocks.Clear();
                rtbOutput.Document.Blocks.Add(newParagraph);
            }
        });
    }

    private ScrollViewer GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer) return depObj as ScrollViewer;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        var regex = NumberRegex();
        e.Handled = regex.IsMatch(e.Text);
    }

    [GeneratedRegex("[^0-9]+")]
    private static partial Regex NumberRegex();
}