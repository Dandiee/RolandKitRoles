using System.Windows;
using KitRoles.ViewModels;

namespace KitRoles;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ((MainViewModel)DataContext).ConnectCommand.Execute(null);
    }
}
