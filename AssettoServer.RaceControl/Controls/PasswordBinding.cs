using System.Windows;
using System.Windows.Controls;

namespace AssettoServer.RaceControl.Controls;

public static class PasswordBinding
{
    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBinding));

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password",
        typeof(string),
        typeof(PasswordBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static readonly DependencyProperty AttachProperty = DependencyProperty.RegisterAttached(
        "Attach",
        typeof(bool),
        typeof(PasswordBinding),
        new PropertyMetadata(false, OnAttachChanged));

    public static string GetPassword(DependencyObject element) => (string)element.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);
    public static bool GetAttach(DependencyObject element) => (bool)element.GetValue(AttachProperty);
    public static void SetAttach(DependencyObject element, bool value) => element.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        if ((bool)args.OldValue)
        {
            passwordBox.PasswordChanged -= HandlePasswordChanged;
        }
        if ((bool)args.NewValue)
        {
            passwordBox.PasswordChanged += HandlePasswordChanged;
        }
    }

    private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PasswordBox passwordBox || (bool)passwordBox.GetValue(IsUpdatingProperty))
        {
            return;
        }

        passwordBox.Password = args.NewValue as string ?? string.Empty;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs args)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(IsUpdatingProperty, true);
        SetPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}
