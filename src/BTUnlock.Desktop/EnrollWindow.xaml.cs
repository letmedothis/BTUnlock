using System.Windows;
using BTUnlock.Desktop.Core;

namespace BTUnlock.Desktop;

public partial class EnrollWindow : Window
{
    public EnrollWindow()
    {
        InitializeComponent();
        TbUser.Text = Environment.UserName;
        TbDomain.Text = Environment.UserDomainName;
        var enrolled = CredentialStore.TryLoad();
        if (enrolled is not null)
        {
            TbUser.Text = enrolled.User;
            TbDomain.Text = enrolled.Domain;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var user = TbUser.Text.Trim();
        var domain = TbDomain.Text.Trim();
        var password = PbPassword.Password;
        var confirm = PbConfirm.Password;

        if (user.Length == 0 || password.Length == 0)
        {
            MessageBox.Show(this, "用户名和密码不能为空。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (password != confirm)
        {
            MessageBox.Show(this, "两次输入的密码不一致。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // 管道协议用 ';' 与换行做分隔，字段里不能出现
        if (user.Contains(';') || domain.Contains(';') || user.Contains('\n') || domain.Contains('\n'))
        {
            MessageBox.Show(this, "用户名/域名不能包含分号或换行。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            CredentialStore.Enroll(user, domain, password);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(this,
            "已录入。请确认凭据提供程序已安装（docs/04），然后锁屏并把手机拿开再回来测试。\n" +
            "提醒：解锁失败不影响正常密码登录。",
            "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        CredentialStore.Clear();
        MessageBox.Show(this, "已清除，人来解锁将不再返回凭据。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = false;
    }
}
