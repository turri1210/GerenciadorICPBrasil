using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using ConfigAuditoria.Models;

namespace ConfigAuditoria.Views;

public partial class UserAccountsPlanDialog : Window
{
    private static readonly Regex UserNameRegex = new("^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);
    private readonly ObservableCollection<EditableUserAccountItem> _accounts = new();

    public UserAccountsPlanDialog(IReadOnlyList<LocalUserAccountPlanItem> existingAccounts)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var item in existingAccounts.OrderBy(account => account.UserName, StringComparer.OrdinalIgnoreCase))
        {
            _accounts.Add(new EditableUserAccountItem(
                item.UserName,
                item.PasswordMask,
                ToProfileLabel(item.Profile),
                item.IsExistingAccount,
                item.NewUserDefinition));
        }

        ProfileComboBox.SelectedItem = ProfileOptions[1];
    }

    public ObservableCollection<EditableUserAccountItem> Accounts => _accounts;

    public IReadOnlyList<string> ProfileOptions { get; } = new[] { "Administrador", "Usuário" };

    public UserAccountsPlan? Result { get; private set; }

    private void OnAddUserClick(object sender, RoutedEventArgs e)
    {
        FeedbackTextBlock.Text = string.Empty;

        var userName = UserNameTextBox.Text.Trim();
        var password = PasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            FeedbackTextBlock.Text = "Informe o nome de usuário.";
            UserNameTextBox.Focus();
            return;
        }

        if (!UserNameRegex.IsMatch(userName))
        {
            FeedbackTextBlock.Text = "O nome de usuário deve conter apenas letras, números ou os caracteres ._-";
            UserNameTextBox.Focus();
            return;
        }

        if (_accounts.Any(item => string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            FeedbackTextBlock.Text = $"O usuário '{userName}' já está listado.";
            UserNameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            FeedbackTextBlock.Text = "Informe a senha do usuário.";
            PasswordBox.Focus();
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            FeedbackTextBlock.Text = "A confirmação de senha não confere.";
            ConfirmPasswordBox.Focus();
            return;
        }

        var profile = ProfileComboBox.SelectedItem?.ToString() ?? ProfileOptions[1];
        var newDefinition = new LocalUserDefinition(
            userName,
            null,
            null,
            password,
            false,
            false,
            false,
            false);

        var newItem = new EditableUserAccountItem(userName, "********", profile, false, newDefinition);
        _accounts.Add(newItem);

        UserNameTextBox.Clear();
        PasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        ProfileComboBox.SelectedItem = ProfileOptions[1];

        AccountsDataGrid.SelectedItem = newItem;
        AccountsDataGrid.ScrollIntoView(newItem);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var validationMessage = BuildValidationMessage();
        if (!string.IsNullOrEmpty(validationMessage))
        {
            FeedbackTextBlock.Text = validationMessage;
            return;
        }

        Result = new UserAccountsPlan(_accounts
            .OrderBy(item => item.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new LocalUserAccountPlanItem(
                item.UserName,
                item.PasswordMask,
                ToProfile(item.Profile),
                item.IsExistingAccount,
                item.NewUserDefinition))
            .ToList());

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string? BuildValidationMessage()
    {
        if (_accounts.Count == 0)
        {
            return "A configuração exige no mínimo uma conta Administrador e uma conta Usuário.";
        }

        var duplicateNames = _accounts
            .GroupBy(item => item.UserName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            return "Existem usuários duplicados na configuração: " + string.Join(", ", duplicateNames) + ".";
        }

        var adminCount = _accounts.Count(item => ToProfile(item.Profile) == LocalUserProfile.Administrator);
        var userCount = _accounts.Count(item => ToProfile(item.Profile) == LocalUserProfile.User);

        if (adminCount == 0)
        {
            return "A configuração exige exatamente 1 conta com perfil Administrador.";
        }

        if (adminCount > 1)
        {
            return "A configuração permite apenas 1 conta com perfil Administrador.";
        }

        if (userCount == 0)
        {
            return "A configuração exige ao menos 1 conta com perfil Usuário.";
        }

        return null;
    }

    private static string ToProfileLabel(LocalUserProfile profile) =>
        profile == LocalUserProfile.Administrator ? "Administrador" : "Usuário";

    private static LocalUserProfile ToProfile(string profileLabel) =>
        string.Equals(profileLabel, "Administrador", StringComparison.OrdinalIgnoreCase)
            ? LocalUserProfile.Administrator
            : LocalUserProfile.User;

    public sealed class EditableUserAccountItem : INotifyPropertyChanged
    {
        private string _profile;

        public EditableUserAccountItem(
            string userName,
            string passwordMask,
            string profile,
            bool isExistingAccount,
            LocalUserDefinition? newUserDefinition)
        {
            UserName = userName;
            PasswordMask = passwordMask;
            _profile = profile;
            IsExistingAccount = isExistingAccount;
            NewUserDefinition = newUserDefinition;
        }

        public string UserName { get; }
        public string PasswordMask { get; }
        public bool IsExistingAccount { get; }
        public LocalUserDefinition? NewUserDefinition { get; }

        public string Profile
        {
            get => _profile;
            set
            {
                if (string.Equals(_profile, value, StringComparison.Ordinal))
                {
                    return;
                }

                _profile = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
