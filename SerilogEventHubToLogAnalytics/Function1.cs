using System;
using System.Configuration;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace SerilogEventHubToLogAnalytics
{
    public static class Function1
    {
        private static readonly string WorkspaceId = ConfigurationManager.AppSettings["WorkspaceId"];
        private static readonly string SharedKey = ConfigurationManager.AppSettings["SharedKey"];

        private static readonly string LogName = ConfigurationManager.AppSettings["LogName"];
        private static readonly HttpClient Client = new HttpClient();
        private static bool Debug => Debugger.IsAttached;

        [FunctionName("SeriLogEventHubToLogAnalytics")]

        public static async Task Run([EventHubTrigger("myeventhub", Connection = "myvmeventhub_RootManageSharedAccessKey_EVENTHUB", ConsumerGroup = "$Default")]string myEventHubMessage, TraceWriter log)
        {
            var eventMessage = JObject.Parse(myEventHubMessage);


            //Better checking needs to be done here for various JSON object types
            if (eventMessage["MessageTemplate"] == null)
            {
                log.Info($"Not a serilog message {eventMessage}");
            }

            var newEvent = new LogAnalyticsEvent
            {
                TimeCurrent = eventMessage["Timestamp"].ToString(),
                SourceSystem = "FunctionApp",
                Level = eventMessage["Level"].ToString(),
                MessageTemplate = eventMessage["MessageTemplate"].ToString(),
                Computer = eventMessage["Properties"]["MachineName_s"].ToString()
            };

            var result = JsonConvert.SerializeObject(newEvent);

            var datestring = DateTime.UtcNow.ToString("r");
            var timestamp = eventMessage["Timestamp"].ToString();
            var stringToHash = "POST\n" + result.Length + "\napplication/json\n" + "x-ms-date:" + datestring +
                               "\n/api/logs";
            var hashedString = BuildSignature(stringToHash, SharedKey);
            var signature = "SharedKey " + WorkspaceId + ":" + hashedString;

            await PostDataAsync(signature, datestring, timestamp, result, log);
        }
        public static string BuildSignature(string message, string secret)
        {

            var encoding = new ASCIIEncoding();
            var keyByte = Convert.FromBase64String(secret);
            var messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                var hash = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hash);
            }
        }

        public static async Task PostDataAsync(string signature, string date, string timestamp, string json,
           TraceWriter log)
        {
            try
            {
                var url = "https://" + WorkspaceId +
                          ".ods.opinsights.azure.com/api/logs?api-version=2016-04-01";

                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url)))
                {
                    HttpContent httpContent = new StringContent(json, Encoding.UTF8);
                    httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    request.Content = httpContent;
                    request.Headers.Add("Accept", "application/json");
                    request.Headers.Add("Log-Type", LogName);
                    request.Headers.Add("Authorization", signature);
                    request.Headers.Add("x-ms-date", date);
                    request.Headers.Add("time-generated-field", timestamp);

                    using (var response = await Client.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorResponse = await response.Content.ReadAsStringAsync();

                            log.Info("Status code: " + response.StatusCode);
                            log.Warning("Error response: " + errorResponse);
                        }
                        else
                        {
                            if (Debug) log.Info("Status code: " + response.StatusCode);
                        }
                    }
                }
            }
            catch (Exception excep)
            {
                log.Warning("API Post Exception: " + excep.Message);
            }
        }
    }
}
