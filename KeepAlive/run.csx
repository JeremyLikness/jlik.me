using System;
using System.Net.Http;

public static void Run(TimerInfo myTimer, TraceWriter log)
{
    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");
    var client = new HttpClient();
    client.GetAsync("https://jlikme.azurewebsites.net/api/UrlRedirect/xxxxxx").Wait();
}
