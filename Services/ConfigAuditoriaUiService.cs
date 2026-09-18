using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace GerenciadorIcpBrasil.Services;

public sealed class ConfigAuditoriaUiService : IConfigAuditoriaUiService
{
    private static readonly Regex UserNameRegex = new("^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);

    private readonly Func<XamlRoot?> _xamlRootProvider;
    private readonly Func<UIElement?> _captureElementProvider;
    private readonly DispatcherQueue _dispatcherQueue;

    public ConfigAuditoriaUiService(Func<XamlRoot?> xamlRootProvider, Func<UIElement?> captureElementProvider)
    {
        _xamlRootProvider = xamlRootProvider;
        _captureElementProvider = captureElementProvider;
        _dispatcherQueue = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
    }

    public Task<bool> ConfirmAsync(string message) =>
        RunOnUIAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "ConfigAuditoria",
                Content = message,
                PrimaryButtonText = "Sim",
                CloseButtonText = "Nao",
                XamlRoot = _xamlRootProvider(),
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        });

    public Task<LocalUserDefinition?> PromptNewUserAsync(bool isAdmin) =>
        RunOnUIAsync(async () =>
        {
            var userName = new TextBox { PlaceholderText = "Usuário" };
            var fullName = new TextBox { PlaceholderText = "Nome completo" };
            var description = new TextBox { PlaceholderText = "Descrição" };
            var password = new PasswordBox();
            var confirm = new PasswordBox();

            var mustChange = new CheckBox
            {
                Content = "O usuário deve alterar a senha no próximo logon",
                IsChecked = true
            };
            var cannotChange = new CheckBox
            {
                Content = "O usuário não pode alterar a senha"
            };
            var neverExpires = new CheckBox
            {
                Content = "A senha nunca expira"
            };
            var disabled = new CheckBox
            {
                Content = "Conta desativada"
            };

            var infoText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235)),
                TextWrapping = TextWrapping.Wrap
            };

            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock
            {
                Text = isAdmin ? "Novo usuário administrador" : "Novo usuário padrão",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Informe os dados do novo usuário.",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
            });

            stack.Children.Add(userName);
            stack.Children.Add(fullName);
            stack.Children.Add(description);
            stack.Children.Add(password);
            stack.Children.Add(confirm);
            stack.Children.Add(mustChange);
            stack.Children.Add(cannotChange);
            stack.Children.Add(neverExpires);
            stack.Children.Add(disabled);
            stack.Children.Add(infoText);

            var dialog = new ContentDialog
            {
                Title = "ConfigAuditoria",
                Content = stack,
                PrimaryButtonText = "Criar",
                CloseButtonText = "Cancelar",
                XamlRoot = _xamlRootProvider(),
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                infoText.Text = "";
                if (string.IsNullOrWhiteSpace(userName.Text))
                {
                    infoText.Text = "Informe o nome de usuário.";
                    args.Cancel = true;
                    return;
                }

                if (string.IsNullOrWhiteSpace(password.Password))
                {
                    infoText.Text = "Informe a senha.";
                    args.Cancel = true;
                    return;
                }

                if (!string.Equals(password.Password, confirm.Password, StringComparison.Ordinal))
                {
                    infoText.Text = "As senhas nao conferem.";
                    args.Cancel = true;
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return new LocalUserDefinition(
                userName.Text.Trim(),
                string.IsNullOrWhiteSpace(fullName.Text) ? null : fullName.Text.Trim(),
                string.IsNullOrWhiteSpace(description.Text) ? null : description.Text.Trim(),
                password.Password,
                mustChange.IsChecked == true,
                cannotChange.IsChecked == true,
                neverExpires.IsChecked == true,
                disabled.IsChecked == true);
        });

    public Task<UserAccountsPlan?> PromptUserAccountsPlanAsync(IReadOnlyList<LocalUserAccountPlanItem> existingAccounts) =>
        RunOnUIAsync(async () =>
        {
            var rows = existingAccounts
                .Select(account => new EditableLocalUserAccount(
                    account.UserName,
                    account.PasswordMask,
                    account.Profile,
                    account.IsExistingAccount,
                    account.NewUserDefinition))
                .OrderBy(account => account.UserName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var accountsHost = new StackPanel { Spacing = 6 };
            var feedbackText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 28, 28)),
                TextWrapping = TextWrapping.Wrap
            };

            var userNameTextBox = new TextBox { PlaceholderText = "Nome de usuário" };
            var passwordBox = new PasswordBox { PlaceholderText = "Senha" };
            var confirmPasswordBox = new PasswordBox { PlaceholderText = "Confirmar senha" };
            var profileComboBox = new ComboBox
            {
                ItemsSource = new[] { "Administrador", "Usuário" },
                SelectedIndex = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            void RenderAccountsTable()
            {
                accountsHost.Children.Clear();

                var headerGrid = CreateTableRow();
                AddCell(headerGrid, "Usuário", 0, true);
                AddCell(headerGrid, "Senha", 1, true);
                AddCell(headerGrid, "Perfil", 2, true);
                accountsHost.Children.Add(headerGrid);

                foreach (var row in rows)
                {
                    var rowGrid = CreateTableRow();
                    AddCell(rowGrid, row.UserName, 0, false);
                    AddCell(rowGrid, row.PasswordMask, 1, false);

                    var roleCombo = new ComboBox
                    {
                        ItemsSource = new[] { "Administrador", "Usuário" },
                        SelectedIndex = row.Profile == LocalUserProfile.Administrator ? 0 : 1,
                        MinWidth = 150
                    };
                    roleCombo.SelectionChanged += (_, _) =>
                    {
                        row.Profile = roleCombo.SelectedIndex == 0
                            ? LocalUserProfile.Administrator
                            : LocalUserProfile.User;
                    };

                    Grid.SetColumn(roleCombo, 2);
                    rowGrid.Children.Add(roleCombo);
                    accountsHost.Children.Add(rowGrid);
                }
            }

            var addUserButton = new Button
            {
                Content = "Criar",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            addUserButton.Click += (_, _) =>
            {
                feedbackText.Text = string.Empty;

                var userName = userNameTextBox.Text.Trim();
                var password = passwordBox.Password;
                var confirmPassword = confirmPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(userName))
                {
                    feedbackText.Text = "Informe o nome de usuário.";
                    return;
                }

                if (!UserNameRegex.IsMatch(userName))
                {
                    feedbackText.Text = "O nome de usuário deve conter apenas letras, numeros ou os caracteres ._-";
                    return;
                }

                if (rows.Any(item => string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                {
                    feedbackText.Text = $"O usuário '{userName}' já está listado.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    feedbackText.Text = "Informe a senha do usuário.";
                    return;
                }

                if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
                {
                    feedbackText.Text = "A confirmação de senha não confere.";
                    return;
                }

                var profile = profileComboBox.SelectedIndex == 0
                    ? LocalUserProfile.Administrator
                    : LocalUserProfile.User;

                var definition = new LocalUserDefinition(
                    userName,
                    null,
                    null,
                    password,
                    false,
                    false,
                    false,
                    false);

                rows.Add(new EditableLocalUserAccount(userName, "********", profile, false, definition));
                rows.Sort((left, right) => string.Compare(left.UserName, right.UserName, StringComparison.OrdinalIgnoreCase));

                userNameTextBox.Text = string.Empty;
                passwordBox.Password = string.Empty;
                confirmPasswordBox.Password = string.Empty;
                profileComboBox.SelectedIndex = 1;

                RenderAccountsTable();
            };

            RenderAccountsTable();

            var addUserGrid = new Grid
            {
                RowSpacing = 8,
                ColumnSpacing = 8
            };
            addUserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addUserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addUserGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            addUserGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            addUserGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            addUserGrid.Children.Add(userNameTextBox);
            Grid.SetColumn(profileComboBox, 1);
            addUserGrid.Children.Add(profileComboBox);

            Grid.SetRow(passwordBox, 1);
            addUserGrid.Children.Add(passwordBox);
            Grid.SetRow(confirmPasswordBox, 1);
            Grid.SetColumn(confirmPasswordBox, 1);
            addUserGrid.Children.Add(confirmPasswordBox);

            Grid.SetRow(addUserButton, 2);
            Grid.SetColumnSpan(addUserButton, 2);
            addUserGrid.Children.Add(addUserButton);

            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = "Somente uma conta pode permanecer como Administrador. As demais devem ficar como Usuário.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = "A configuração minima e 1 Administrador e 1 Usuário.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new ScrollViewer
            {
                Content = accountsHost,
                MaxHeight = 280
            });
            content.Children.Add(new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 231, 235)),
                Margin = new Thickness(0, 4, 0, 4)
            });
            content.Children.Add(new TextBlock
            {
                Text = "Adicionar novo usuário",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            content.Children.Add(addUserGrid);
            content.Children.Add(feedbackText);

            var dialog = new ContentDialog
            {
                Title = "ConfigAuditoria - Contas de usuário",
                Content = content,
                PrimaryButtonText = "Aplicar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _xamlRootProvider(),
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                var validationMessage = BuildUserAccountsPlanValidationMessage(rows);
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    feedbackText.Text = validationMessage;
                    args.Cancel = true;
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var outputRows = rows
                .Select(row => new LocalUserAccountPlanItem(
                    row.UserName,
                    row.PasswordMask,
                    row.Profile,
                    row.IsExistingAccount,
                    row.NewUserDefinition))
                .ToList();

            return new UserAccountsPlan(outputRows);
        });

    public Task ShowInfoAsync(string message) =>
        RunOnUIAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "ConfigAuditoria",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = _xamlRootProvider(),
            };
            await dialog.ShowAsync();
            return true;
        });

    public Task ShowErrorAsync(string message) =>
        RunOnUIAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "ConfigAuditoria",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = _xamlRootProvider(),
            };
            await dialog.ShowAsync();
            return true;
        });

    public async Task SaveEvidenceAsync(Func<string, string> reportBuilder)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var suggestedName = $"Evidencias_{timestamp}.txt";

        string? reportPath = null;
        string? screenshotPath = null;
        var usedFallback = false;

        try
        {
            var file = await PickReportFileAsync(suggestedName);
            if (file != null)
            {
                reportPath = file.Path;
            }
        }
        catch
        {
            reportPath = null;
        }

        if (string.IsNullOrWhiteSpace(reportPath))
        {
            usedFallback = true;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            reportPath = Path.Combine(desktop, "Evidencias.txt");
        }

        var directory = Path.GetDirectoryName(reportPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        screenshotPath = Path.Combine(directory, $"Evidencia_{timestamp}.png");

        var reportText = reportBuilder(screenshotPath);
        Directory.CreateDirectory(directory);
        await File.AppendAllTextAsync(reportPath, reportText, Encoding.UTF8);

        var captureOk = await TryCaptureAsync(screenshotPath);
        var message = usedFallback
            ? $"Não foi possivel utilizar o local escolhido. As evidências foram salvas na Área de Trabalho:\n{reportPath}\n{screenshotPath}"
            : $"Evidências salvas em:\n{reportPath}\n{screenshotPath}";

        if (!captureOk)
        {
            message += "\n(Observação: não foi possivel capturar a tela automaticamente.)";
        }

        await ShowInfoAsync(message);
    }

    private Task<StorageFile?> PickReportFileAsync(string suggestedName) =>
        RunOnUIAsync(async () =>
        {
            var picker = new FileSavePicker();
            picker.SuggestedFileName = suggestedName;
            picker.FileTypeChoices.Add("Arquivo de texto", new List<string> { ".txt" });
            InitializeWithWindow(picker);
            var file = await picker.PickSaveFileAsync();
            return (StorageFile?)file;
        });

    private static Grid CreateTableRow()
    {
        var rowGrid = new Grid
        {
            ColumnSpacing = 8
        };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return rowGrid;
    }

    private static void AddCell(Grid rowGrid, string text, int column, bool isHeader)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };

        Grid.SetColumn(textBlock, column);
        rowGrid.Children.Add(textBlock);
    }

    private static string? BuildUserAccountsPlanValidationMessage(IReadOnlyList<EditableLocalUserAccount> rows)
    {
        var adminCount = rows.Count(row => row.Profile == LocalUserProfile.Administrator);
        var userCount = rows.Count(row => row.Profile == LocalUserProfile.User);

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

    private void InitializeWithWindow(object obj)
    {
        var window = App.MainWindow;
        if (window == null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(obj, hwnd);
    }

    private async Task<bool> TryCaptureAsync(string path)
    {
        var element = _captureElementProvider();
        if (element is null)
        {
            return false;
        }

        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(element);

            var buffer = await rtb.GetPixelsAsync();
            var pixels = buffer.ToArray();

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var raStream = stream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, raStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth,
                (uint)rtb.PixelHeight,
                96,
                96,
                pixels);
            await encoder.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<T> RunOnUIAsync<T>(Func<Task<T>> func)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            return func();
        }

        var tcs = new TaskCompletionSource<T>();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private sealed class EditableLocalUserAccount
    {
        public EditableLocalUserAccount(
            string userName,
            string passwordMask,
            LocalUserProfile profile,
            bool isExistingAccount,
            LocalUserDefinition? newUserDefinition)
        {
            UserName = userName;
            PasswordMask = passwordMask;
            Profile = profile;
            IsExistingAccount = isExistingAccount;
            NewUserDefinition = newUserDefinition;
        }

        public string UserName { get; }
        public string PasswordMask { get; }
        public LocalUserProfile Profile { get; set; }
        public bool IsExistingAccount { get; }
        public LocalUserDefinition? NewUserDefinition { get; }
    }
}
