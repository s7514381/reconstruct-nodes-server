using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Utility
{
    public class MailSender
    {
        public MailSenderConfig _config = new MailSenderConfig();

        public MailSender(IConfiguration configuration)
        {
            configuration.GetSection("SmtpConfig").Bind(_config);
            if (string.IsNullOrWhiteSpace(_config.Password))
            {
                throw new InvalidOperationException("SmtpConfig:Password is not configured.");
            }
        }

        public Task SentAsync(string toEmail, string subject, string message)
        {
            return SentAsync(toEmail, null, subject, message);
        }

        public async Task SentAsync(string toEmail, IEnumerable<string>? ccEmail, string subject, string message)
        {
            using var client = new SmtpClient();
            try
            {
                MimeMessage Message = new MimeMessage();
                Message.From.Add(new MailboxAddress(_config.SenderName, _config.Sender ?? throw new InvalidOperationException("寄件者信箱未設定。")));
                Message.To.Add(MailboxAddress.Parse(toEmail.Trim()));

                if (ccEmail != null)
                {
                    foreach (var email in ccEmail)
                    {
                        if (!string.IsNullOrEmpty(email))
                        {
                            Message.Cc.Add(MailboxAddress.Parse(email.Trim()));
                        }
                    }
                }

                Message.Subject = subject;
                Message.Body = new TextPart(MimeKit.Text.TextFormat.Html)
                {
                    Text = message
                };

                client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                await client.ConnectAsync(_config.Server ?? throw new InvalidOperationException("SMTP 位置未設定。"), _config.Port);
                await client.AuthenticateAsync(
                    _config.Account ?? throw new InvalidOperationException("SMTP 帳號未設定。"),
                    _config.Password ?? throw new InvalidOperationException("SMTP 密碼未設定。"));
                await client.SendAsync(Message);
            }
            catch (Exception) { }
            finally 
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true);
                }
            }
        }
    }

    public class MailSenderConfig
    {
        public string? SenderName { get; set; }
        /// <summary>
        /// SMTP 位置
        /// </summary>
        public string? Server { get; set; }

        /// <summary>
        /// SMTP Port
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// SMTP 帳號
        /// </summary>
        public string? Account { get; set; }

        /// <summary>
        /// SMTP 密碼
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 寄件者信箱
        /// </summary>
        public string? Sender { get; set; }
    }

}