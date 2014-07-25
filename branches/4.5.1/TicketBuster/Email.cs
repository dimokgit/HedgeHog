using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mail;
using System.Net;

namespace TicketBuster {
  public static class Emailer {
    public static void Send(string from,string to,string password, string subject,string body) {
      var fromAddress = new MailAddress(from, from);
      var toAddress = new MailAddress(to, to);
      var smtp = new SmtpClient {
        Host = "smtp.gmail.com",
        Port = 587,
        EnableSsl = true,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        Credentials = new NetworkCredential(fromAddress.Address, password),
        Timeout = 20000
      };
      using (var message = new MailMessage(fromAddress, toAddress) {
        Subject = subject,
        Body = body
      }) {
        smtp.Send(message);
      }
    }
  }
}
