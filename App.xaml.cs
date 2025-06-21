using System.Windows;

namespace CourseWork
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Створюємо та показуємо сплеш-скрін
            SplashScreenWindow splashScreen = new SplashScreenWindow();
            splashScreen.Show();

            // 2. Створюємо головне вікно, але поки не показуємо його
            MainWindow mainWindow = new MainWindow();

            // 3. Підписуємось на подію закриття сплеш-скріна
            splashScreen.Closed += (s, args) =>
            {
                // 4. Коли сплеш-скрін закрився, показуємо головне вікно
                mainWindow.Show();
            };
        }
    }
}