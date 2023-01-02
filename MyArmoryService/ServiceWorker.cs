
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyArmoryService.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MyArmoryService;

class ServiceWorker : BackgroundService
{

    private readonly ILogger<ServiceWorker> _logger;
    /// <summary>
    /// @TODO Search for game install directory.
    /// </summary>
    private readonly string arcDpsFile = @"C:\Program Files\Guild Wars 2\bin64\d3d9.dll";

    public ServiceWorker(ILogger<ServiceWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(10000, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {

            _logger.LogInformation($"{DateTimeOffset.Now}: Worker running");
            CheckDpsMeter();
            ParseDpsLogs(GetBosses());
            await Task.Delay(60000, stoppingToken);
        }
    }

    private void ParseDpsLogs(List<RaidBoss> raidBosses)
    {
        string[] allfiles = Directory.GetFiles($"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\Guild Wars 2\\addons\\arcdps\\arcdps.cbtlogs", "*.evtc", SearchOption.AllDirectories);
        foreach (var file in allfiles)
        {
            FileInfo info = new FileInfo(file);

            if (info.IsReadOnly || File.Exists(info.FullName + ".uploaded"))
            {
                _logger.LogInformation($"{DateTimeOffset.Now}: SKIP {info.FullName}.\n");
            }
            else
            {
                _logger.LogInformation($"{DateTimeOffset.Now}: Parsing {info.FullName}.\n");

                var result = UploadEvtc(info.FullName);
                if (result != null && result?.Permalink != null)
                {
                    if (result.Permalink.Contains("Golem"))
                    {
                        SubmitDpsReportUrl(result.Permalink);
                    }
                    else
                    {
                        SubmitRaidReportUrl(result.Permalink);
                    }

                    File.Create(info.FullName + ".uploaded");
                    _logger.LogInformation($"{DateTimeOffset.Now}: Uploaded {raidBosses.SingleOrDefault(b => b.Id == result?.Encounter?.BossId)?.Name}.\n");
                }
                else
                {
                    _logger.LogError($"{DateTimeOffset.Now}: Failed to process {info.FullName}, skipping.\n");
                }
            }
        }
    }

    private UploadContentDto UploadEvtc(string file)
    {
        Uri uri = new Uri($"https://dps.report/uploadContent?json=1&userToken={GetDpsReportUserTokenAsync().Result.UserToken}");

        WebClient x = new WebClient();
        byte[] responseArray = x.UploadFile(uri, file);
        return JsonConvert.DeserializeObject<UploadContentDto>(System.Text.Encoding.ASCII.GetString(responseArray), new JsonSerializerSettings
        {
            Error = (se, ev) => { ev.ErrorContext.Handled = true; },
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        });
    }

    private List<RaidBoss> GetBosses()
    {
        List<RaidBoss> raidBosses = new List<RaidBoss>();
        Uri uri = new Uri($"https://dps.report/docs/bossIds.txt");

        WebClient x = new WebClient();
        string bossList = x.DownloadString(uri);
        string[] lines = bossList.Split(
              new[] { Environment.NewLine },
              StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }
            else
            {
                string[] parts = line.Split(" - ");

                try
                {
                    raidBosses.Add(new RaidBoss { Id = int.Parse(parts[0]), Name = parts[2] });
                }
                catch (Exception)
                {
                    _logger.LogError($"{DateTimeOffset.Now}: Failed to process {parts[0]}, skipping.\n");
                }

            }
        }

        return raidBosses;
    }

    private void SubmitRaidReportUrl(string reportUri)
    {
        var uri = new Uri($"https://localhost:8443/api/Report/raid/{Uri.EscapeDataString(reportUri)}");
        using var webClient = new WebClient();
        try
        {
            webClient.UploadString(uri,
            WebRequestMethods.Http.Put,
            "");
        }
        catch (Exception)
        {
            _logger.LogError($"Failed to Put {uri}");
        }
    }

    private void SubmitDpsReportUrl(string reportUri)
    {
        var uri = new Uri($"https://localhost:8443/api/Report/golem/{Uri.EscapeDataString(reportUri)}");
        using var webClient = new WebClient();
        try
        {
            webClient.UploadString(uri,
            WebRequestMethods.Http.Put,
            "");
        }
        catch (Exception)
        {
            _logger.LogError($"Failed to Put {uri}");
        }
    }

    private async Task<DpsReportUserToken> GetDpsReportUserTokenAsync()
    {
        using (var client = new HttpClient())
        {
            var url = new Uri("https://dps.report/getUserToken");

            var response = await client.GetAsync(url);
            string json;
            using (var content = response.Content)
            {
                json = await content.ReadAsStringAsync();
            }
            return JsonConvert.DeserializeObject<DpsReportUserToken>(json, new JsonSerializerSettings
            {
                Error = (se, ev) => { ev.ErrorContext.Handled = true; },
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            });
        }
    }

    private class DpsReportUserToken
    {
        [JsonProperty("userToken")]
        public string UserToken { get; set; }
    }

    private void CheckDpsMeter()
    {
        string url = "https://www.deltaconnected.com/arcdps/x64/d3d9.dll.md5sum";

        _logger.LogInformation($"{DateTimeOffset.Now}: Polling arcDPS repository.");
        string remoteChecksum = FetchRemoteHash(url);
        string localChecksum = File.Exists(arcDpsFile) ? CalculateMD5(arcDpsFile) : null;

        if (remoteChecksum == localChecksum)
        {
            _logger.LogInformation($"{DateTimeOffset.Now}: Installed arcDPS is the most recent version.");
        }
        else
        {
            InstallArcDps(arcDpsFile);
        }
    }

    private void InstallArcDps(string fileName)
    {
        _logger.LogInformation($"{DateTimeOffset.Now}: Fetching most recent version of arcDPS.");

        WebClient webClient = new WebClient();
        try
        {
            if (File.Exists(arcDpsFile))
            {
                File.Delete(fileName);
            }
            webClient.DownloadFile("https://www.deltaconnected.com/arcdps/x64/d3d9.dll", fileName);
        }
        catch (Exception e)
        {
            _logger.LogInformation($"{DateTimeOffset.Now}: {e.Message}");
        }

        _logger.LogInformation($"{DateTimeOffset.Now}: Installed the latest build of arcDPS.");
    }

    private string FetchRemoteHash(string url)
    {
        WebClient webClient = new WebClient();
        try
        {
            string hash = webClient.DownloadString(url);
            hash = hash.Replace("  x64/d3d9.dll", "").Trim();
            return hash;
        }
        catch (Exception e)
        {
            _logger.LogInformation($"{DateTimeOffset.Now}: {e.Message}");
        }
        return null;
    }

    static string CalculateMD5(string filename)
    {
        using (var md5 = MD5.Create())
        {
            using (var stream = File.OpenRead(filename))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}