using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MobileCentreUpload
{
    public class Program
    {
        // Example command line
        // -A ApplicationName -T 111111111 -U AUserName -L AFileLocaton.ipa -D Collaborators 
        // You need to define a distribution group for your release to appear
        static AutoResetEvent autoResetEvent = new AutoResetEvent(false);

        private const int commandValueStarts = 1;
        private const string ApplicationCommand = "-A";
        private const string TokenCommand = "-T";
        private const string FileCommand = "-L";
        private const string UserCommand = "-U";
        private const string ReleaseNotesCommand = "-RN";
        private const string DistributionGroupCommand = "-D";

        static void Main(string[] args)
        {
            // -T -L -U
            // 
            var applicationName = GetCommandParam(args, ApplicationCommand);
            var apiToken = GetCommandParam(args, TokenCommand);
            var fileLocation = GetCommandParam(args, FileCommand);
            var userName = GetCommandParam(args, UserCommand);
            var releaseNotes = "This is is a release";
            var distributionGroup = GetCommandParam(args, DistributionGroupCommand);

            PerformReleaseUpload(applicationName, userName, fileLocation, apiToken, distributionGroup, releaseNotes);

            autoResetEvent.WaitOne();

            Console.WriteLine("All Done");
        }

        private static string GetCommandParam(string[] args, string commandToken)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == commandToken)
                {
                    if (args.Length >= index + commandValueStarts)
                    {
                        return args[index + commandValueStarts];
                    }
                }
            }

            return string.Empty;
        }

        private static async Task PerformReleaseUpload(string applicationName, string userName, string fileLocation, string apiToken, string distributionGroup, string releaseNotes)
        {
            try
            {
                Console.WriteLine("Getting Upload Details");
                var uploadDetails = await GetUploadDetails(applicationName, userName, apiToken);

                Console.WriteLine("Uploading File");
                await UploadFile(fileLocation, uploadDetails);

                Console.WriteLine("Letting Mobile Centre Know We Have Uploaded");
                var releaseDetails = await FinalizeUpload(applicationName, userName, apiToken, uploadDetails);

                Console.WriteLine("Send the release to a distribution group");
                await DistributeRelease(apiToken, distributionGroup, releaseNotes, releaseDetails);

                autoResetEvent.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private static async Task DistributeRelease(string apiToken, string distributionGroup, string releaseNotes,
            (int releaseid, string releaseUrl) releaseDetails)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var requestUploadComplete =
                    new StringContent(
                        $@"{{ ""destination_name"": ""{distributionGroup}"", ""release_notes"" : ""{releaseNotes}"" }}");

                requestUploadComplete.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                requestUploadComplete.Headers.Add("X-API-Token", new[] {apiToken});

                var httpMethodPatch = new HttpMethod("PATCH");

                var requestMessage =
                    new HttpRequestMessage(httpMethodPatch, $"https://api.mobile.azure.com/{releaseDetails.releaseUrl}");

                requestMessage.Content = requestUploadComplete;

                var responseMessage = await client.SendAsync(requestMessage);

                var result = await responseMessage.Content.ReadAsStringAsync();

                Console.WriteLine(result);
            }
        }

        private static async Task<(int releaseid, string releaseUrl)> FinalizeUpload(string applicationName, string userName, string apiToken, (string uploadUrl, string uploadId) uploadDetails)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var requestUploadComplete = new StringContent(@"{ ""status"": ""committed"" }");

                requestUploadComplete.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                requestUploadComplete.Headers.Add("X-API-Token", new[] {apiToken});

                var httpMethodPatch = new HttpMethod("PATCH");

                var requestMessage = new HttpRequestMessage(httpMethodPatch,
                    $"https://api.mobile.azure.com/v0.1/apps/{userName}/{applicationName}/release_uploads/{uploadDetails.uploadId}");

                requestMessage.Content = requestUploadComplete;

                var responseMessage = await client.SendAsync(requestMessage);

                if (responseMessage.IsSuccessStatusCode)
                {
                    var result = await responseMessage.Content.ReadAsStringAsync();

                    Console.WriteLine(result);

                    JsonReader reader = new JsonTextReader(new StringReader(result));
                    reader.DateParseHandling = DateParseHandling.None;
                    var responseRelease = JObject.Load(reader);
                    // Get release id
                    // release url
                    return (responseRelease.Value<int>("release_id"), responseRelease.Value<string>("release_url"));
                }
            }

            return (-1, string.Empty);
        }

        private static async Task UploadFile(string fileLocation, (string uploadUrl, string uploadId) uploadDetails)
        {
            using (HttpClient client = new HttpClient())
            {
                FileInfo file = new FileInfo(fileLocation);

                var fileBytes = File.ReadAllBytes(fileLocation);

                var uploadFile = new MultipartFormDataContent();

                uploadFile.Add(new StreamContent(new MemoryStream(fileBytes)), "ipa", file.Name);

                var uploadResponse = await client.PostAsync(uploadDetails.uploadUrl, uploadFile);

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    var uploadResponseContent = uploadResponse.Content.ReadAsStringAsync();
                    Console.WriteLine(uploadResponseContent);
                }
            }
        }

        private static async Task<(string uploadUrl, string uploadId)> GetUploadDetails(string applicationName, string userName, string apiToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var requestForUploadURL = new StringContent(String.Empty);

                requestForUploadURL.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                requestForUploadURL.Headers.Add("X-API-Token", new[] {apiToken});

                var responseMessage = await client.PostAsync(
                    $"https://api.mobile.azure.com/v0.1/apps/{userName}/{applicationName}/release_uploads",
                    requestForUploadURL);

                var uploadLinkData = await responseMessage.Content.ReadAsStringAsync();

                Console.WriteLine(uploadLinkData);

                JsonReader reader = new JsonTextReader(new StringReader(uploadLinkData));
                reader.DateParseHandling = DateParseHandling.None;
                var responseUpload = JObject.Load(reader);

                return (responseUpload.Value<string>("upload_url"), responseUpload.Value<string>("upload_id"));
            }
        }
    }
}
