using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace CourseWork
{
    public partial class LogWindow : Window
    {
        public ObservableCollection<LogEntry> LogEntries { get; }

        public LogWindow(ObservableCollection<LogEntry> logEntries)
        {
            InitializeComponent();
            LogEntries = logEntries;
            this.DataContext = this;
        }

        public void ScrollToBottom()
        {
            if (LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Owner as MainWindow;
            Action clearAction = () =>
            {
                LogEntries.Clear();
                mainWindow?.LogEvent("Лог було очищено.");
            };

            if (mainWindow != null)
            {
                mainWindow.ShowCustomMessageBox("Ви впевнені, що хочете очистити лог?", "Очистити лог", MessageType.Confirmation, null, clearAction);
            }
            else
            {
                if (MessageBox.Show("Ви впевнені, що хочете очистити лог?", "Очистити лог", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    clearAction();
                }
            }
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (LogListBox.SelectedItems.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (LogEntry item in LogListBox.SelectedItems.Cast<LogEntry>())
                {
                    sb.AppendLine(item.Message);
                }
                try
                {
                    Clipboard.SetText(sb.ToString());
                    ShowInfoMessageBox($"{LogListBox.SelectedItems.Count} запис(ів) скопійовано до буферу обміну.", "Копіювання");
                }
                catch (Exception ex)
                {
                    ShowInfoMessageBox($"Не вдалося скопіювати: {ex.Message}", "Помилка копіювання");
                }
            }
            else
            {
                ShowInfoMessageBox("Будь ласка, оберіть записи для копіювання.", "Немає обраних записів");
            }
        }

        private void ShowInfoMessageBox(string message, string caption)
        {
            var mainWindow = Owner as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.ShowCustomMessageBox(message, caption);
            }
            else
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}