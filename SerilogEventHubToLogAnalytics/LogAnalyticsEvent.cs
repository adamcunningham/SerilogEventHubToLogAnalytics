namespace SerilogEventHubToLogAnalytics
{
    class LogAnalyticsEvent
    {
        public string TimeCurrent { get; set; }
        public string SourceSystem { get; set; }
        public string Level { get; set; }
        public string MessageTemplate { get; set; }
        public string Computer { get; set; }
    }
}
