# 邮件发送配置 — GARCHINA 账户管理系统

邮件发送功能说明文档，涵盖 SMTP 配置、发送方式、邮件模板和触发场景。

---

## 1. SMTP 配置

所有配置集中在 `appsettings.json` 的 `EmailSettings` 节：

```json
"EmailSettings": {
  "SmtpServer": "mail1.sinarmas-agri.com",
  "SmtpPort": 587,
  "FromAddress": "fox.tang@golden-agri.com",
  "ToAddress": "fox.tang@golden-agri.com",
  "CcAddress": "kabin.su@golden-agri.com",
  "Username": "fox.tang@golden-agri.com",
  "Password": "YourRealPasswordHere"
}
```

| 字段 | 说明 |
|------|------|
| `SmtpServer` | SMTP 服务器地址 |
| `SmtpPort` | 587（STARTTLS 标准端口） |
| `FromAddress` | 发件人地址 |
| `ToAddress` | 默认收件人（通常是 IT 管理员） |
| `CcAddress` | 默认抄送人 |
| `Username` | SMTP 认证用户名 |
| `Password` | SMTP 认证密码 |

代码中通过依赖注入读取：

```csharp
public class MyPageModel : PageModel
{
    private readonly IConfiguration _configuration;
    public MyPageModel(IConfiguration configuration) { _configuration = configuration; }

    void SendEmail()
    {
        var server = _configuration["EmailSettings:SmtpServer"];
        var port = int.Parse(_configuration["EmailSettings:SmtpPort"]);
        // ...
    }
}
```

---

## 2. 发送模式

**所有邮件都是"发射后不管"（fire-and-forget）模式**，通过 `Task.Run()` 异步发送，不阻塞页面响应：

```csharp
_ = Task.Run(() =>
{
    try
    {
        ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
        using var client = new SmtpClient(server, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };
        using var msg = new MailMessage(from, to)
        {
            Subject = subject,
            Body = body,
            BodyEncoding = System.Text.Encoding.UTF8,
            IsBodyHtml = false
        };
        msg.CC.Add(cc);
        client.Send(msg);
    }
    catch (Exception ex)
    {
        // 记录发送失败，不抛出
    }
});
```

**注意：** `ServerCertificateValidationCallback` 绕过 SSL 证书验证（接受自签名证书），生产环境可考虑改为正确验证。

---

## 3. 邮件模板与触发场景

项目共 6 个页面触发邮件，7 种邮件模板：

### 3.1 密码自助修改通知 — `Pages/Index.cshtml.cs`

**触发：** 用户在首页自助修改密码
**收件人：** 用户本人的 AD 邮箱（`user.EmailAddress`）
**主题：** `[IT信息] 用户AD账号密码更新通知`

```
尊敬的用户，

您的 GARCHINA 账号 {employeeId} 密码已更新。
新密码为：{newPassword}

此密码适用于：
- GARCHINA 系统认证
- China OA 系统
- GARCHINA VPN
- Workday 请休假系统

特别注意：
1. 复制粘贴密码时，请先粘贴到记事本，检查是否有空格。
2. 输入密码后，点击密码框旁的小眼睛图标确认输入正确。
3. 请尽快更新默认密码，密码更新后会自动同步至邮箱系统。
4. ChinaOA、Workday系统登录时请注意用户名格式。

如有问题，请联系中国区 IT 部门：
邮箱：CN_IT_Support@sinarmas-agri.com

此邮件由系统自动发送，请勿回复。
```

---

### 3.2 入职申请通知 — `Pages/Onboard.cshtml.cs`

**触发：** 用户提交入职申请
**收件人：** `ToAddress`（IT 管理员）+ 抄送 `CcAddress`
**主题：** `[IT信息] 新入职申请 - {中文名}({英文名})`

```
[新入职申请]

申请编号: {req.Id}
中文名: {req.CnName}
英文名: {req.EnName}
员工编号: {req.EmployeeId}
手机号: {req.Mobile}
所属区域: {req.Region}
申请邮箱: {req.NeedEmail}
直接上级邮箱: {req.ManagerEmail}
回传邮箱: {req.ContactEmail}
开通VPN: 是/否
提交时间: {req.SubmitTime}

请登录管理员页面进行审批：https://www.garchina.com/account/Admin/Request

此邮件由系统自动发送，请勿回复。
```

---

### 3.3 入职审批-用户创建通知 — `Pages/Admin/Request.cshtml.cs`

**触发：** 管理员批准入职申请并创建 AD 用户
**收件人：** `ToAddress` + 抄送 `CcAddress`；如果用户提供了回传邮箱则额外发送一封
**主题：** `[IT信息] 新用户AD账号创建通知`

```
尊敬的用户，

您的 GARCHINA 账号已创建，信息如下：
姓名: {cnName}({enName})
员工号: {employeeId}
手机号: {mobile}
所属区域: {region}
企业邮箱: {emailAddr}
密码: {newPassword}

此账号适用于 GARCHINA 系统认证、China OA 系统、GARCHINA VPN、Workday 请休假系统。
请尽快登录并修改密码。密码有效期 90 天。

邮箱账号需要雅加达邮箱管理团队创建，请留意后续邮件。

如有问题，请联系中国区 IT 部门：CN_IT_Support@sinarmas-agri.com
此邮件由系统自动发送，请勿回复。
```

---

### 3.4 手动创建用户通知 — `Pages/Admin/NewUser.cshtml.cs`

**触发：** 管理员在"手动创建用户"页面创建 AD 用户
**收件人：** `ToAddress` + 抄送 `CcAddress`
**主题：** `[IT信息] 新用户AD账号创建通知`

内容与 3.3 类似，差异是不含手机号和区域字段。

---

### 3.5 密码重置通知（单个） — `Pages/Admin/UserAdmin.cshtml.cs`

**触发：** 管理员在 UserAdmin 页面重置单个用户密码
**收件人：** `ToAddress` + 抄送 `CcAddress`
**主题：** `[IT信息] 用户AD 账号密码重置通知`

内容与 3.1 基本相同，末尾多一行服务器标识：
```
此邮件由系统 [10.95.0.62] 自动发送，请勿回复。
```

---

### 3.6 批量密码重置通知 — `Pages/Admin/BatchUser.cshtml.cs`

**触发：** 管理员在批量用户管理页面批量重置密码
**收件人：** `ToAddress` + 抄送 `CcAddress`
**主题：** `[IT信息] 批量密码重置通知`

```
以下用户密码已批量重置：

{id1} | {name1} | 新密码: {pwd1}
{id2} | {name2} | 新密码: {pwd2}
...

此密码适用于 GARCHINA 系统认证、China OA 系统、GARCHINA VPN、Workday 请休假系统。
请通知相关用户尽快修改密码。密码有效期 90 天。

此邮件由系统自动发送，请勿回复。
```

---

## 4. 触发场景汇总

| 页面 | 操作 | 发件时机 | 收件人 |
|------|------|---------|--------|
| 首页（Index） | 用户自助改密码 | 密码修改成功后 | 用户本人（AD 邮箱） |
| 入职申请（Onboard） | 提交入职申请 | 申请提交后 | 管理员 + 抄送 |
| 入职审批（Request） | 批准并创建用户 | AD 用户创建后 | 管理员 + 抄送 + 用户 |
| 手动创建用户（NewUser） | 创建 AD 用户 | 用户创建后 | 管理员 + 抄送 |
| 用户管理（UserAdmin） | 重置密码 | 密码重置后 | 管理员 + 抄送 |
| 批量管理（BatchUser） | 批量重置密码 | 全部重置完成后 | 管理员 + 抄送 |

**多数邮件发送给管理员而非最终用户**，由管理员转达。只有 Index（自助改密码）和 Request（入职审批带回传邮箱）会直接发给用户。

---

## 5. 发送记录

邮件发送状态持久化到 `App_Data/` 目录：

| 页面 | 记录文件 | 页面展示位置 |
|------|---------|-------------|
| UserAdmin | `email_status.dat` | "邮件发送记录" 面板 |
| NewUser | `newuser_email_status.dat` | "邮件发送状态" 面板 |

记录内容包括成功/失败状态和错误信息，JSON 格式存储，`FileProtection` 加密。

---

## 6. 技术要点

- **库：** `System.Net.Mail`（.NET 内置，不需要 NuGet 包）
- **发送方式：** 同步 `client.Send()` 包在 `Task.Run()` 中异步触发
- **编码：** `System.Text.Encoding.UTF8`
- **格式：** 纯文本 `IsBodyHtml = false`
- **SSL：** `EnableSsl = true`，`ServerCertificateValidationCallback` 始终返回 `true`
- **无重试机制：** 发送失败仅记录日志，不重试
