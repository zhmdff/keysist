using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keysist.Views;

public sealed partial class RegisterDevicePage : Page
{
    public RegisterDevicePage()
    {
        InitializeComponent();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.GoBackOrNavigateToHub("Devices");
    }
}
