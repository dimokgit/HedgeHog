using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mail;
using System.Net;
using System.IO;
using System.Net.Mime;
using System.Text.RegularExpressions;
using IX = ImapX;

namespace HedgeHog.Cloud {
  public static class Emailer {
    public static void Send(string from, string to, string password, string subject, string body, Tuple<string, string>[] attachments) {
      Send(from, to, password, subject, body
        , attachments.Select(att => Tuple.Create(Encoding.UTF8.GetBytes(att.Item1), att.Item2)).ToArray());
    }
    public static void Send(string from, string to, string password, string subject, string body, params Tuple<byte[], string>[] attachments) {
      if (string.IsNullOrWhiteSpace(from)) throw new Exception("From email is empty");
      if (string.IsNullOrWhiteSpace(to)) throw new Exception("To email is empty");
      if (string.IsNullOrWhiteSpace(password)) throw new Exception("From email password is empty");
      var fromAddress = new MailAddress(from, from);
      var toAddress = new MailAddress(to, to);
      var smtp = new SmtpClient {
        Host = "smtp.gmail.com",
        Port = 587,
        EnableSsl = true,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        Credentials = new NetworkCredential(fromAddress.Address, password),
        Timeout = 20000,
      };
      using (var message = new MailMessage(fromAddress, toAddress) {
        Subject = subject.Replace(Environment.NewLine, " "),
        Body = body,
      }) {
        attachments.ForEach(att =>
          message.Attachments.Add(new Attachment(new MemoryStream(att.Item1), att.Item2, MimeType.GetMimeType(att.Item1, att.Item2))));
        smtp.Send(message);
      }
    }
    public class IMapSearch {
      public DateTimeOffset Since { get; set; }
      public string Subject { get; set; }
      public string From { get; set; }
      public override string ToString() {
        return string.Join(" ", new string[] { 
          Since > DateTimeOffset.MinValue ? "SINCE "+Since.ToString("d-MMM-yyyy") : "",
          !string.IsNullOrWhiteSpace(Subject)?"SUBJECT \""+Subject+"\"":"",
          !string.IsNullOrWhiteSpace(From)?"FROM \""+From+"\"":""
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
      }
    }
    public static IList<ImapX.Message> Read(string userName, string password, string folder, IMapSearch search) {
      return Read(userName, password, folder, search + "");
    }
    public static IList<ImapX.Message> Read(string userName, string password, string folder, string search) {
      var server = "imap.gmail.com";
      using (var client = new IX.ImapClient(server, true, false)) {
        if (client.Connect()) {
          if (client.Login(userName, password)) {
            //var messages = client.Folders.Inbox.Search("UID SEARCH FROM \"13057880763@mymetropcs.com\"");
            try {
              return client.Folders.Inbox.Search(search);
            } catch (Exception exc) {
              throw new ApplicationException(new { userName, folder, server, search } + "", exc);
            }
          } else {
            throw new ApplicationException(new { userName, folder, server, Login = "Error" } + "");
          }
        } else {
          throw new ApplicationException(new { userName, folder, server, Connect = "Error" } + "");
        }
      }
    }
  }
}
