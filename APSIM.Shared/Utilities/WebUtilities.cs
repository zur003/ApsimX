namespace APSIM.Shared.Utilities
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Serialization;


    /// <summary>
    /// A class containing some web utilities
    /// </summary>
    public class WebUtilities
    {
        /// <summary>
        /// HttpClient is intended to be instantiated once per application, rather than per-use. See Remarks.
        /// </summary>
        public static readonly HttpClient client = new HttpClient();
 
#pragma warning disable SYSLIB0014
        /// <summary>
        ///  Upload a file via ftp
        /// </summary>
        /// <param name="localFileName">Name of the file to be uploaded</param>
        /// <param name="username">remote username</param>
        /// <param name="password">remote password</param>
        /// <param name="hostname">remote hostname</param>
        /// <param name="remoteFileName">Full path and name of where the file goes</param>
        /// <returns></returns>
        public static bool UploadFTP(string localFileName, string username, string password, string hostname, string remoteFileName)
        {
            // Get the object used to communicate with the server.
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create("ftp://" + hostname + remoteFileName);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential();

            // Copy the contents of the file to the request stream.
            StreamReader sourceStream = new StreamReader(localFileName);
            byte[] fileContents = Encoding.UTF8.GetBytes(sourceStream.ReadToEnd());
            sourceStream.Close();
            request.ContentLength = fileContents.Length;

            Stream requestStream = request.GetRequestStream();
            requestStream.Write(fileContents, 0, fileContents.Length);
            requestStream.Close();

            FtpWebResponse response = (FtpWebResponse)request.GetResponse();
            string retVal = response.StatusDescription;
            response.Close();

            return retVal != "200";
        }

        /// <summary>
        /// Send a string to the specified socket server. Returns the response string. Will throw
        /// if cannot connect.
        /// </summary>
        public static string SocketSend(string serverName, int port, string data)
        {
            string Response = null;
            TcpClient Server = null;
            try
            {
                Server = new TcpClient(serverName, Convert.ToInt32(port, CultureInfo.InvariantCulture));
                Byte[] bData = System.Text.Encoding.ASCII.GetBytes(data);
                Server.GetStream().Write(bData, 0, bData.Length);

                Byte[] bytes = new Byte[8192];

                // Wait for data to become available.
                while (!Server.GetStream().DataAvailable)
                    Thread.Sleep(10);

                // Loop to receive all the data sent by the client.
                while (Server.GetStream().DataAvailable)
                {
                    int NumBytesRead = Server.GetStream().Read(bytes, 0, bytes.Length);
                    Response += System.Text.Encoding.ASCII.GetString(bytes, 0, NumBytesRead);
                }
            }
            finally
            {
                if (Server != null) Server.Close();
            }
            return Response;
        }

        /// <summary>
        /// Async function to issue POST request for a URL and return the result as a Stream
        /// </summary>
        /// <param name="url">URL to be accessed</param>
        /// <param name="content">Data to be posted, as JSON</param>
        /// <returns></returns>
        private static async Task<Stream> AsyncPostStreamTask(string url, string content)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var data = new StringContent(content, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, data).ConfigureAwait(false);
            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>Call REST web service using POST.</summary>
        /// Assumes the data returned by the URL is JSON, 
        /// which is then deserialised into the returned object
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="url">The URL of the REST service.</param>
        /// <returns>The return data</returns>
        public static T PostRestService<T>(string url)
        {
            var stream = AsyncPostStreamTask(url, "").Result;
            if (typeof(T).Name == "Object")
                return default(T);
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<T>(stream, options);
        }


        /// <summary>
        /// Async function to issue GET request for a URL and return the result as a Stream
        /// </summary>
        /// <param name="url">URL to access</param>
        /// <param name="mediaType">Preferred media type to return</param>
        /// <returns>Data stream obtained from the URL</returns>
        private static async Task<Stream> AsyncGetStreamTask(string url, string mediaType)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
            HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>Call REST web service using GET.
        /// Assumes the data returned by the URL is XML, 
        /// which is then deserialised into the returned object
        /// </summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="url">The URL of the REST service.</param>
        /// <returns>The return data</returns>
        public static T CallRESTService<T>(string url)
        {
            var stream = AsyncGetStreamTask(url, "application/xml").Result;
            if (typeof(T).Name == "Object")
                return default(T);

            XmlSerializer serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(new XmlUtilities.NamespaceIgnorantXmlTextReader(new StreamReader(stream)));
        }

        /// <summary>
        /// Calls a url and returns the web response in a memory stream
        /// </summary>
        /// <param name="url">The url to call</param>
        /// <returns>The data stream</returns>
        public static MemoryStream ExtractDataFromURL(string url)
        {
            try
            {
                MemoryStream result = AsyncGetStreamTask(url, "*/*").Result as MemoryStream;
                if (result == null)
                    throw new Exception();
                return result;
            }
            catch (Exception)
            {
                throw new Exception("Cannot get data from " + url);
            }
        }

#pragma warning restore SYSLIB0014
    }
}
