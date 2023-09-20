using DotnetSpider;
using DotnetSpider.DataFlow;
using DotnetSpider.DataFlow.Parser;
using DotnetSpider.DataFlow.Storage;
using DotnetSpider.Downloader;
using DotnetSpider.HtmlAgilityPack.Css;
using DotnetSpider.Http;
using DotnetSpider.Infrastructure;
using DotnetSpider.Scheduler;
using DotnetSpider.Scheduler.Component;
using DotnetSpider.Selector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NugetSpider
{
    /// <summary>
    /// package包
    /// </summary>
    internal class PackageSpider : Spider
    {
        public static async Task RunAsync()
        {
            var builder = Builder.CreateDefaultBuilder<PackageSpider>();
            builder.UseSerilog();
            builder.UseDownloader<HttpClientDownloader>();
            builder.UseQueueDistinctBfsScheduler<HashSetDuplicateRemover>();
            await builder.Build().RunAsync();
        }

        public PackageSpider(IOptions<SpiderOptions> options, DependenceServices services, ILogger<Spider> logger) : base(options, services, logger)
        {

        }

        protected override async Task InitializeAsync(CancellationToken stoppingToken = default)
        {
            // 添加自定义解析
            AddDataFlow(new Parser());
            // 使用控制台存储器
            AddDataFlow(new ConsoleStorage());
            // 添加采集请求
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nuget.txt");
            HashSet<string> packages = new HashSet<string>();
            if (File.Exists(path))
            {
                CancellationToken cancellationToken = new CancellationToken();
                string[] contents = await File.ReadAllLinesAsync(path, cancellationToken);
                if (contents != null && contents.Length > 0)
                {
                    foreach (var content in contents)
                    {
                        if (string.IsNullOrEmpty(content)) continue;

                        packages.Add(content.Trim());
                    }
                }

            }

            //拼接url，从nuget获取package的所有版本
            string pre = "https://www.nuget.org/packages/";
            List<Request> requests = new List<Request>();
            foreach (var package in packages)
            {
                string url = pre + package.Trim();
                requests.Add(new Request(url)
                {
                    // 请求超时 10 秒
                    Timeout = 10000
                });
            }

            await AddRequestsAsync(requests);

            //下载package包
            AddDataFlow(new PackageFileStorage());
        }

        protected override SpiderId GenerateSpiderId()
        {
            return new(ObjectId.CreateId().ToString(), "Batch Package");
        }

        class Parser : DataParser
        {
            public override Task InitializeAsync()
            {
                return Task.CompletedTask;
            }

            protected override Task ParseAsync(DataFlowContext context)
            {
                var selectable = context.Selectable;
                // 解析数据
                var version_history = selectable.XPath("//div[@class='version-history']");
                var dd = version_history.Select(Selectors.XPath(".//table"));
                var ee = dd.Select(Selectors.XPath(".//tbody[@class='no-border']"));
                var xx = ee.SelectList(Selectors.XPath(".//tr"));
                var urls = new List<string>();
                foreach (var x in xx)
                {
                    var ff = x.Select(Selectors.XPath(".//td//a/@href"))?.Value;

                    if (!string.IsNullOrWhiteSpace(ff))
                    {
                        ff = ff.Trim();

                        if (ff.StartsWith("https://www.nuget.org/packages/"))
                        {
                            var qq = ff.Substring("https://www.nuget.org/packages/".Length);
                            string[] strings = qq.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (strings != null && strings.Length >= 2)
                            {
                                var url = $"https://globalcdn.nuget.org/packages/{strings[0].ToLower()}.{strings[1]}.nupkg";
                                urls.Add(url);
                            }
                        }
                    }
                }

                context.AddData("nuget", urls);

                return Task.CompletedTask;
            }
        }


        class PackageFileStorage : FileStorageBase
        {
            private readonly object _locker = new object();

            public override async Task HandleAsync(DataFlowContext context)
            {
                if (IsNullOrEmpty(context))
                {
                    base.Logger.LogWarning("数据流上下文不包含解析结果");
                    return;
                }

                IDictionary<object, object> data = context.GetData();
                if (data != null && data.ContainsKey("nuget"))
                {
                    object dd = data["nuget"];
                    if (dd is IList<string>)
                    {
                        IList<string> urls = (IList<string>)dd;
                        if (urls != null && urls.Count > 0)
                        {
                            await HttpClientAsync(urls.ToList());
                        }
                    }
                }
            }


            public async Task HttpClientAsync(List<string> urls)
            {
                // 定义保存文件的目录
                string downloadDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");

                // 创建保存文件的目录（如果不存在）
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                }

                var tasks = new List<Task>();
                foreach (var fileUrl in urls)
                {
                    try
                    {
                        if (File.Exists(downloadDirectory + "\\" + Path.GetFileName(fileUrl))) continue;

                        // 创建HttpClient实例
                        var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                        httpClient.DefaultRequestHeaders.Add("cookie", "ARRAffinitySameSite=4e578cbe1efe6783ea8ca73836b126e532ae76861f661df353db6f787c115815; MSCC=cid=jpjq41yjqgaao650k5o4n0e8-c1=2-c2=2-c3=2");
                        httpClient.DefaultRequestHeaders.Add("referer", "https://www.nuget.org");
                        httpClient.DefaultRequestHeaders.Add("authority", "globalcdn.nuget.org");
                        httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36");
                        //tasks.Add();
                        //Thread.Sleep(1000);
                        await DownloadFile(httpClient, fileUrl);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"下载文件时发生错误: {ex.Message}");
                    }
                }

                Task.WaitAll(tasks.ToArray());


            }

            public async Task DownloadFile(HttpClient httpClient, string fileUrl)
            {
                // 发送GET请求并获取响应
                var response = await httpClient.GetAsync(fileUrl);
                if (response != null && response.IsSuccessStatusCode)
                {
                    // 获取响应内容作为字节数组
                    var contentBytes = await response.Content.ReadAsByteArrayAsync();

                    // 从URL中提取文件名
                    string fileName = Path.GetFileName(fileUrl);

                    string downloadDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    // 构造文件保存路径
                    string filePath = Path.Combine(downloadDirectory, "packages", fileName);

                    // 将字节数组保存为文件
                    await File.WriteAllBytesAsync(filePath, contentBytes);

                    Log.Logger.Information($"下载文件: {fileName}");
                }
                else
                {
                    Log.Logger.Information($"无法下载文件: {fileUrl}");
                }
            }

        }
    }
}
