using System;
using System.Text.RegularExpressions;
using System.Windows;
using ConfigAuditoria.Models;

namespace ConfigAuditoria.Views;

public partial class NewUserDialog : Window
{
    private static readonly Regex UserNameRegex = new("^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);

    public NewUserDialog(bool isAdmin)
    {
        InitializeComponent();
        IsAdmin = isAdmin;

        DialogTitleText.Text = isAdmin ? "Novo usuario administrador" : "Novo usuario";
        DialogSubtitleText.Text = isAdmin
            ? "Crie um novo usuario administrativo. Utilize um nome diferente de 'Administrador'."
            : "Crie um novo usuario padrao para os colaboradores.";

        InfoTextBlock.Text = isAdmin
            ? "Esse usuario sera adicionado ao grupo de administradores locais."
            : "Esse usuario tera permissao apenas de usuario padrao (grupo 'Usuarios').";
    }

    public bool IsAdmin { get; }

    public LocalUserDefinition? Result { get; private set; }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var userName = UserNameTextBox.Text.Trim();
        var password = PasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;
        var fullName = string.IsNullOrWhiteSpace(FullNameTextBox.Text) ? null : FullNameTextBox.Text.Trim();
        var description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            MessageBox.Show(this, "Informe o nome de usuario.", "ConfigAuditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
            UserNameTextBox.Focus();
            return;
        }

        if (!UserNameRegex.IsMatch(userName))
        {
            MessageBox.Show(this, "O nome de usuario deve conter apenas letras, numeros ou os caracteres ._-", "ConfigAuditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
            UserNameTextBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Informe a senha do usuario.", "ConfigAuditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "A confirmacao de senha nao confere.", "ConfigAuditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
            ConfirmPasswordBox.Focus();
            return;
        }

        var mustChange = MustChangePasswordCheckBox.IsChecked == true;
        var cannotChange = CannotChangePasswordCheckBox.IsChecked == true;

        if (mustChange && cannotChange)
        {
            MessageBox.Show(this, "Nao e possivel exigir a troca da senha e impedir que o usuario altere a senha ao mesmo tempo.", "ConfigAuditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new LocalUserDefinition(
            userName,
            fullName,
            description,
            password,
            mustChange,
            cannotChange,
            PasswordNeverExpiresCheckBox.IsChecked == true,
            AccountDisabledCheckBox.IsChecked == true);

        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
