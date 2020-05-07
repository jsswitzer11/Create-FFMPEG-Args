using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Web;
using System.Text;

namespace FfmpegArgs
{
    public static class FFmpegArgParser
    {
        private static Settings settings;
        private static string oVidName;
        private static string dVidName;
        private static string kVidName;
        private static string Season;
        private static string Week;
        static IQueueClient queueClient;

        [FunctionName("FFmpegArgParser")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext context)
        {
            try
            {
                GetSettings(context, log);
                //GetVideoBlobAsync(Season, Week);
                GetVideoBlobAsync("rawvideos");
                GetPlayMetadataAsync(log);
                return (ActionResult)new OkObjectResult("Complete");
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.InnerException.ToString());
                return (ActionResult)new BadRequestResult();
            }
        }
        public static async void GetPlayMetadataAsync(ILogger log)
        {
            try
            {

                string rawXML = await GetXMLBlob("xmlscript");
                byte[] encodedString = Encoding.UTF8.GetBytes(rawXML);

                MemoryStream ms = new MemoryStream(encodedString);
                ms.Flush();
                ms.Position = 0;

                XmlDocument doc = new XmlDocument();
                doc.Load(ms);

                string league;
                string SeasonType;
                string gameKey;
                string playID;
                string offMp4 = string.Empty;
                string defMp4 = string.Empty;
                string kicksMp4 = string.Empty;
                string offense = string.Empty;
                string defense = string.Empty;
                string kicks = string.Empty;
                List<Plays> pl = new List<Plays>();
                List<string> playArgs = new List<string>();

                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.Name == "Game")
                    {
                        league = node.Attributes.GetNamedItem("League").Value;
                        Season = node.Attributes.GetNamedItem("Season").Value;
                        SeasonType = node.Attributes.GetNamedItem("SeasonType").Value;
                        Week = node.Attributes.GetNamedItem("Week").Value;

                        foreach (XmlNode game in node.ChildNodes)
                        {
                            if (game.Name == "Media")
                            {
                                var desc = game.Attributes.GetNamedItem("ID").Value;
                                if (desc == "CoachOffense_MIN")
                                {
                                    offense = game.Attributes.GetNamedItem("ID").Value;
                                }
                                else if (desc == "CoachDefense_MIN")
                                {
                                    defense = game.Attributes.GetNamedItem("ID").Value;
                                }
                                else
                                {
                                    kicks = game.Attributes.GetNamedItem("ID").Value;
                                }

                                foreach (XmlNode media in game.ChildNodes)
                                {
                                    switch (desc)
                                    {
                                        case "CoachOffense_MIN":
                                            offMp4 = media.Attributes.GetNamedItem("FileName").Value;
                                            break;
                                        case "CoachDefense_MIN":
                                            defMp4 = media.Attributes.GetNamedItem("FileName").Value;
                                            break;
                                        case "CoachKicks_MIN":
                                            kicksMp4 = media.Attributes.GetNamedItem("FileName").Value;
                                            break;
                                    }
                                }
                            }

                            if (game.Name == "Plays")
                            {
                                foreach (XmlNode plays in game.ChildNodes)
                                {
                                    if (plays.Name == "Play")
                                    {
                                        Plays p = new Plays();
                                        gameKey = plays.Attributes.GetNamedItem("Gamekey").Value;
                                        playID = plays.Attributes.GetNamedItem("PlayId").Value;

                                        foreach (XmlNode play in plays.ChildNodes)
                                        {
                                            p.season = Season;
                                            p.seasonType = SeasonType;
                                            p.league = league;
                                            p.week = Week;
                                            p.gamekey = gameKey;
                                            p.playID = playID;

                                            if (play.Attributes.GetNamedItem("Source").Value.Contains("Sideline"))
                                            {
                                                p.cameraView = "Sideline";
                                            }
                                            else if (play.Attributes.GetNamedItem("Source").Value.Contains("Endzone"))
                                            {
                                                p.cameraView = "Endzone";
                                            }
                                            else
                                            {
                                                p.cameraView = "Scoreboard";
                                            }

                                            p.markInFrame = play.Attributes.GetNamedItem("MarkInFrame").Value;
                                            p.markoutFrame = play.Attributes.GetNamedItem("MarkOutFrame").Value;

                                            string ffmpeg_file = Path.Combine(settings.outputPath, p.outputName);
                                            string id = play.Attributes.GetNamedItem("ID").Value;
                                            string arg = $"-ss {p.markInFrame.ToString()} -t {p.duration.ToString()} {ffmpeg_file}";
                                            playArgs.Add(id + " -- " + arg);

                                            if (play.Attributes.GetNamedItem("ID").Value == offense)
                                            {
                                                p.videoSource = offMp4;
                                            }
                                            else if (play.Attributes.GetNamedItem("ID").Value == defense)
                                            {
                                                p.videoSource = defMp4;
                                            }
                                            else
                                            {
                                                p.videoSource = kicksMp4;
                                            }

                                            if (p.cameraView.Contains("Scoreboard")) { }
                                            else
                                            {
                                                pl.Add(p);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                //string oFile = Path.Combine(settings.outputPath, "offense_ffmpeg_params.txt");
                //string dFile = Path.Combine(settings.outputPath, "defense_ffmpeg_params.txt");
                //string kFile = Path.Combine(settings.outputPath, "kicks_ffmpeg_params.txt");
                //using (StreamWriter ofile = new StreamWriter(oFile))
                //{
                    foreach (string pa in playArgs)
                    {
                        if (pa.Contains("CoachOffense"))
                        {
                            queueClient = new QueueClient(settings.ServiceBusConnectionString, "offense");
                            string offMessageBody = $"-i {oVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3);
                            var offMessage = new Message(Encoding.UTF8.GetBytes(offMessageBody));

                            await queueClient.SendAsync(offMessage);

                            await queueClient.CloseAsync();

                            //ofile.WriteLine($"-i {oVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3));
                    }
                    }
               // }
                //using (StreamWriter dfile = new StreamWriter(dFile))
                //{
                    foreach (string pa in playArgs)
                    {
                        if (pa.Contains("CoachDefense"))
                        {
                            queueClient = new QueueClient(settings.ServiceBusConnectionString, "defense");
                            string defMessageBody = $"-i {dVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3);
                            var defMessage = new Message(Encoding.UTF8.GetBytes(defMessageBody));

                            await queueClient.SendAsync(defMessage);

                            await queueClient.CloseAsync();

                            //dfile.WriteLine($"-i {dVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3));
                    }
                    }
               // }
                //using (StreamWriter kfile = new StreamWriter(kFile))
                //{
                    foreach (string pa in playArgs)
                    {
                        if (pa.Contains("CoachKicks"))
                        {
                            queueClient = new QueueClient(settings.ServiceBusConnectionString, "specialteams");
                            string kickMessageBody = $"-i {kVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3);
                            var kickMessage = new Message(Encoding.UTF8.GetBytes(kickMessageBody));

                            await queueClient.SendAsync(kickMessage);

                            await queueClient.CloseAsync();

                            //kfile.WriteLine($"-i {kVidName} " + pa.Substring(pa.LastIndexOf("-- ") + 3));
                    }
                    }
               // }

                //WritePlayVideoBlob("offense_ffmpeg_params.txt", oFile, log);
                //WritePlayVideoBlob("defense_ffmpeg_params.txt", dFile, log);
                //WritePlayVideoBlob("kicks_ffmpeg_params.txt", kFile, log);

                ms.Close();

            }
            catch (Exception ex)
            {
                log.LogInformation(ex.InnerException.ToString());
            }

        }

        public static async void GetVideoBlobAsync(string seasonContainerName)//, string weekFileName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(settings.outputStorageAccountConnStr);

            var serviceClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer inputContainer = serviceClient.GetContainerReference($"{seasonContainerName}");

            //Get file from the following format: Season/Week/raw
            //var inputDirectory = inputContainer.GetDirectoryReference($"{weekFileName}/raw/");

            //var blobItem = await inputDirectory.ListBlobsSegmentedAsync(null);
            var blobItem = await inputContainer.ListBlobsSegmentedAsync(null);
            foreach (var item in blobItem.Results)
            {
                var blob = (CloudBlockBlob)item;
                if (blob.Name.Contains(" O "))
                {
                    var oPath = settings.storageAccountName + "/" + inputContainer.Name + "/" + blob.Name + settings.sasToken;
                    oVidName = HttpUtility.UrlPathEncode(oPath);
                }
                else if (blob.Name.Contains(" D "))
                {
                    var dPath = settings.storageAccountName + "/" + inputContainer.Name + "/" + blob.Name + settings.sasToken;
                    dVidName = HttpUtility.UrlPathEncode(dPath);
                }
                else if (blob.Name.Contains(" K "))
                {
                    var kPath = settings.storageAccountName + "/" + inputContainer.Name + "/" + blob.Name + settings.sasToken;
                    kVidName = HttpUtility.UrlPathEncode(kPath);
                }
            }
        }

        public static async Task<string> GetXMLBlob(string inputContainerName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(settings.outputStorageAccountConnStr);
            // Connect to the blob storage
            var serviceClient = storageAccount.CreateCloudBlobClient();
            // Connect to the input
            CloudBlobContainer inputContainer = serviceClient.GetContainerReference($"{inputContainerName}");
            // Connect to the blob file
            var blobItem = await inputContainer.ListBlobsSegmentedAsync(null);
            foreach (var item in blobItem.Results)
            {
                var blob = (CloudBlockBlob)item;
                string contents = blob.DownloadTextAsync().Result;
                return contents;
            }
            return null;
        }

        //static void WritePlayVideoBlob(string filename, string filepath, ILogger log)
        //{
        //    try
        //    {
        //        BlobServiceClient blobServiceClient = new BlobServiceClient(settings.outputStorageAccountConnStr);
        //        BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(settings.blobContainerName);

        //        BlobClient blobClient = blobContainerClient.GetBlobClient(filename);

        //        FileStream uploadFileStream = File.OpenRead(filepath);
        //        blobClient.Upload(uploadFileStream);
        //        uploadFileStream.Close();
        //    }
        //    catch (Exception ex)
        //    {
        //        log.LogInformation(ex.Message);
        //    }
        //}
        static void GetSettings(ExecutionContext context, ILogger log)
        {
            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                settings = new Settings();

                if (String.IsNullOrEmpty(config["outputPath"]))
                {
                    settings.outputPath = Path.GetTempPath();
                }
                else
                {
                    settings.outputPath = config["outputPath"];
                }

                settings.outputStorageAccountConnStr = config["VikingsStorageAccount"];
                settings.storageAccountName = config["storageAccountName"];
                settings.sasToken = config["sasToken"];
                settings.ServiceBusConnectionString = config["ServiceBusConnectionString"];
                settings.blobContainerName = "ffmpegargs";
                if (!String.IsNullOrEmpty(config["blobContainerName"]))
                {
                    settings.blobContainerName = config["blobContainerName"];
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
            }
        }
    }
    class Settings
    {
        public string outputPath { get; set; }
        public string outputStorageAccountConnStr { get; set; }
        public string storageAccountName { get; set; }
        public string blobContainerName { get; set; }
        public string sasToken { get; set; }
        public string ServiceBusConnectionString { get; set; }
    }
}
