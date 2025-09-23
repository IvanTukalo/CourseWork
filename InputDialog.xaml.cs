using System.Windows;
using System.Windows.Input;

namespace CourseWork
{
    public partial class InputDialog : Window
    {
        public string Answer => InputTextBox.Text;

        public InputDialog(string question, string title = "Введення", string defaultAnswer = "")
        {
            InitializeComponent();
            this.Title = title;
            QuestionText.Text = question;
            InputTextBox.Text = defaultAnswer;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        }
    }
}