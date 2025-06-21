using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace CourseWork
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Анімація плавного з'явлення
            DoubleAnimation fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5));
            this.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);

            // Затримка на 3 секунди
            await Task.Delay(3000);

            // Анімація плавного зникнення
            DoubleAnimation fadeOutAnimation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.5));
            fadeOutAnimation.Completed += (s, _) =>
            {
                // Закриваємо вікно після завершення анімації
                this.Close();
            };
            this.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
        }
    }
}