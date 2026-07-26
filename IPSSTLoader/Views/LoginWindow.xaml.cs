using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IPSSTLoader.Views;

public partial class LoginWindow : Window
{
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;
    public bool LoginConfirmed { get; private set; } = false;

    public LoginWindow()
    {
        InitializeComponent();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        Username = UsernameBox.Text;
        Password = PasswordBox.Password;
        LoginConfirmed = true;
        Close();
    }

    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(this, new RoutedEventArgs());
        }
    }
}
