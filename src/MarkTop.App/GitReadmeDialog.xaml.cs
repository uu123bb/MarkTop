using System.Windows;
using MarkTop.Core.Configuration;

namespace MarkTop.App;

public partial class GitReadmeDialog : Window
{
    public GitReadmeDialog(string userName, string commitMessage)
    {
        InitializeComponent();
        UserNameBox.Text = userName;
        CommitMessageBox.Text = string.IsNullOrWhiteSpace(commitMessage)
            ? GitSettings.DefaultCommitMessage
            : commitMessage;
        Loaded += (_, _) =>
        {
            UserNameBox.Focus();
            UserNameBox.SelectAll();
        };
    }

    public string UserName => UserNameBox.Text.Trim();

    public string CommitMessage => string.IsNullOrWhiteSpace(CommitMessageBox.Text)
        ? GitSettings.DefaultCommitMessage
        : CommitMessageBox.Text.Trim();

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            MessageBox.Show(this, "请填写 GitHub 用户名。", "MarkTop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
