using DotnetSpider.Scheduler.Component;
using DotnetSpider.Scheduler;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace NugetSpider
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //设置线程池
            ThreadPool.SetMaxThreads(255, 255);
            ThreadPool.SetMinThreads(255, 255);

            //设置日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console().WriteTo.File("logs/spider.log")
                .CreateLogger();

            await PackageSpider.RunAsync();

            Console.WriteLine("Bye!");
        }
    }
}