using System.Collections.Generic;

namespace MQTTToSQLServer.Models
{
    public class NotificationSettings
    {
        // Email Settings
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string SenderEmail { get; set; }
        public string SenderPassword { get; set; }
        public string RecipientEmail { get; set; }
        public bool EnableEmailNotification { get; set; } = false;

        // Wablas WhatsApp Settings
        public string WablasServerUrl { get; set; }
        public string WablasToken { get; set; }
        public string WablasSecretKey { get; set; }
        public List<string> WablasPhoneNumbers { get; set; } = new List<string>();
        public bool EnableWhatsAppNotification { get; set; } = false;

        // Alert Settings
        public bool SendInstantAlert { get; set; } = true;
        public bool SendHourlyReport { get; set; } = true;
        public bool SendDailyReport { get; set; } = false;
        public bool SendMonthlyReport { get; set; } = false;
        public int HourlyReportInterval { get; set; } = 0;
        public string DailyReportTime { get; set; } = "08:00";
        public int MonthlyReportDay { get; set; } = 1;
        public string MonthlyReportTime { get; set; } = "08:00";
    }
}
