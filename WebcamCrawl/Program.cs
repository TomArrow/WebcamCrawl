using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WebcamCrawl
{
    class Program
    {


        public static Dictionary<string, string> namedUrls = new Dictionary<string, string>();
        public static Dictionary<string, string> namedExtensions = new Dictionary<string, string>();
        public static Dictionary<string, int> namedTimeouts = new Dictionary<string, int>();
        public static Dictionary<string, int> namedRequestTimeouts = new Dictionary<string, int>();
        public static Dictionary<string, long> lastGrab = new Dictionary<string, long>();
        public static Dictionary<string, long> lastGrabErrored = new Dictionary<string, long>();
        public static Dictionary<string, string> lastHashes = new Dictionary<string, string>(); // name of webcam => last CRC
        public static Dictionary<string, List<string>> lastHashesAll = new Dictionary<string, List<string>>(); // name of webcam => last CRC


        static void Main(string[] args)
        {

            if (File.Exists("webcams.txt"))
            {
                string[] urls = File.ReadAllLines("webcams.txt");
                //string regexp = @"^(.+?),(.+?)=(.+?)$";
                string regexp = @"^(.+?),(.+?)=([^,]+)(?:,(\d+))?(?:,(.+))?$"; // updated to include request timeout and file ending
                foreach(string url in urls)
                {
                    if (url[0] == '#') // Commented out
                    {
                        continue;
                    }
                    MatchCollection matches = Regex.Matches(url,regexp,RegexOptions.IgnoreCase);
                    try
                    {
                        string name = matches[0].Groups[1].Value;
                        int timeout = int.Parse(matches[0].Groups[2].Value);
                        namedTimeouts.Add(name, timeout);
                        string thisurl = matches[0].Groups[3].Value;
                        int thisRequestTimeout = matches[0].Groups[4].Value == "" ? 1000 : int.Parse(matches[0].Groups[4].Value);
                        namedRequestTimeouts.Add(name, thisRequestTimeout);
                        string thisFileEnding = matches[0].Groups[5].Value == "" ? "jpg" : matches[0].Groups[5].Value;
                        namedUrls.Add(name, thisurl);
                        namedExtensions.Add(name, thisFileEnding);
                        if (!Directory.Exists("scrapes"))
                        {
                            Directory.CreateDirectory("scrapes");
                        }
                        if (!Directory.Exists("scrapes/" + name))
                        {
                            Directory.CreateDirectory("scrapes/" + name);
                        }
                    } catch(Exception e)
                    {
                        Console.Write("malformed webcams.txt");
                    }
                }

                if(namedUrls.Count > 0)
                {

                    fuckyou(args);
                }

            }
            else
            {
                Console.WriteLine("webcams.txt not found");
            }

        }
        static async Task fuckyou(string[] args)
        {

            //string url = args[0];
            do
            {

                foreach (KeyValuePair<string, string> entry in namedUrls)
                {

                    await DoCrawl(entry.Key,entry.Value);
                }
                System.Threading.Thread.Sleep(500);
            } while (true);
        }


        public static SHA1CryptoServiceProvider hasher = new SHA1CryptoServiceProvider();

        public static long errorRetryTimeoutInSeconds = 10;

        static async Task DoCrawl(string name, string url)
        {
            
            long timestampSeconds = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
                
            try
            {

                string timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds().ToString();


                long lastTime = 0;
                lastGrab.TryGetValue(name, out lastTime);

                long lastTimeErrored = 0;
                lastGrabErrored.TryGetValue(name, out lastTimeErrored);
                
                if(lastTime+namedTimeouts[name] > timestampSeconds || lastTimeErrored + errorRetryTimeoutInSeconds > timestampSeconds)
                {
                    return; // Not yet time to do it for this one.
                }


                url = url.Replace("{rand}", timestamp);

                //Console.WriteLine(url);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                // access req.Headers to get/set header values before calling GetResponse. 
                // req.CookieContainer allows you access cookies.
                req.Timeout = namedRequestTimeouts[name];
                
                var response = req.GetResponse();
                Stream responseStream = response.GetResponseStream();
                MemoryStream copyDestination = new MemoryStream();
                responseStream.CopyTo(copyDestination);
                byte[] data = copyDestination.ToArray();

                string hash = Convert.ToBase64String(hasher.ComputeHash(data));

                string oldHash = "";
                lastHashes.TryGetValue(name, out oldHash);

                bool skippedBcHashDupe = false;

                if (hash == oldHash)
                {
                    skippedBcHashDupe = true;
                    Console.WriteLine("skipping '" + name + "', same checksum as last one");
                }else if (lastHashesAll.ContainsKey(name) && lastHashesAll[name].Contains(hash))
                {
                    skippedBcHashDupe = true;
                    Console.WriteLine("skipping '" + name + "', same checksum as earlier item (buggy webcam)");
                }
                else
                {
                    string filename = "scrapes\\" + name + "\\" + timestamp + "."+namedExtensions[name];
                    File.WriteAllBytes(filename, data);
                    Console.WriteLine("Saved '" + filename+"'");
                }

                if (!lastHashesAll.ContainsKey(name))
                {
                    lastHashesAll[name] = new List<string>();
                }

                if (!skippedBcHashDupe)
                {
                    lastHashesAll[name].Add(hash);
                }

                lastHashes[name] = hash;
                lastGrab[name] = timestampSeconds;


            } catch(Exception e)
            {
                Console.WriteLine(e.Message+","+ name);

                lastGrabErrored[name] = timestampSeconds;
            }
            

        }

    
    }
}
