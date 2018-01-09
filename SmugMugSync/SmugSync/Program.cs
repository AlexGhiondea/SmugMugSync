using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.XPath;
using CommandLine;
using OutputColorizer;
using SmugMug.v2.Authentication;
using SmugMug.v2.Authentication.Tokens;
using SmugMug.v2.Types;

namespace SmugSync
{
    class Program
    {
        private static OAuthToken s_oauthToken;
        private static Options s_options;

        private static SiteEntity s_site;
        private static UserEntity s_user;

        static void Main(string[] args)
        {
            if (!Parser.TryParse(args, out s_options))
            {
                return;
            }

            s_oauthToken = ConsoleAuthentication.GetOAuthTokenFromProvider(new FileTokenProvider());
            Debug.Assert(!s_oauthToken.Equals(OAuthToken.Invalid));

            s_site = new SiteEntity(s_oauthToken);
            s_user = s_site.GetAuthenticatedUserAsync().Result;


            switch (s_options.Action)
            {
                case CommandAction.ListAlbums:
                    ListAlbums();
                    break;
                case CommandAction.SyncAlbum:
                    SyncAlbum();
                    break;
                default:
                    Colorizer.WriteLine("[Red!Error]: Invalid option [White!{0}]", s_options.Action);
                    return;
            }
        }

        private static void SyncAlbum()
        {
            var album = s_user.GetAlbumByIdAsync(s_options.AlbumId).Result;

            if (album == null)
            {
                Colorizer.WriteLine("[Red!Error]: Could not find album with id [Yellow!{0}]", s_options.AlbumId);
                return;
            }

            if (!Directory.Exists(s_options.OutputFolder))
            {
                Colorizer.WriteLine("[Red!Error]: Could not find output folder [Yellow!{0}]", s_options.OutputFolder);
                return;
            }

            Colorizer.WriteLine("Retrieving information about the images in the album: [Yellow!{0}] ", album.Name);
            // Find all the photos from the album.
            var imagesOnSiteRaw = album.GetImagesAsync().Result;

            // filter our the images.
            var imagesOnSite = new List<ImageEntity>();
            if (s_options.Tags != null)
            {
                foreach (var image in imagesOnSiteRaw)
                {
                    bool hasAtLeastATag = false;
                    foreach (var tag in s_options.Tags)
                    {
                        if (image.Keywords.ToLower().Contains(tag.ToLower()))
                        {
                            hasAtLeastATag = true;
                            break;
                        }
                    }

                    if (hasAtLeastATag)
                        imagesOnSite.Add((image));
                }
            }

            Colorizer.WriteLine("Found [Yellow!{0}] images in the album", imagesOnSite.Count);

            FileInfo[] filesOnDisk = new DirectoryInfo(s_options.OutputFolder).GetFiles();

            List<string> fileNames = filesOnDisk.Select(x => x.Name.ToLower()).ToList();

            Colorizer.WriteLine("Found [Yellow!{0}] images on disk.", filesOnDisk.Length);


            // match the files on disk with the files on the site.
            List<ImageEntity> filesToDownload = new List<ImageEntity>();

            foreach (var image in imagesOnSite)
            {
                if (fileNames.Contains(image.FileName.ToLower()))
                    continue;

                filesToDownload.Add(image);
            }

            Colorizer.WriteLine("Found [Yellow!{0}] files not locally.", filesToDownload.Count);


            foreach (var image in filesToDownload)
            {
                image.ImageKey = image.ImageKey + "-1";
                var imageSizes = image.GetImageSizesAsync().Result;

                string downloadUri;
                if (imageSizes == null)
                    downloadUri = image.ArchivedUri;
                else
                    downloadUri = imageSizes.OriginalImageUrl;
                
                //if (imageSizes == null)
                //{
                //    downloadUri = 
                //}
                //else
                //    downloadUri = imageSizes.OriginalImageUrl;

                Colorizer.WriteLine("Using [White!{0}]", downloadUri);
                Colorizer.Write("Downloading [Yellow!{0}] ([White!{1}] bytes)... ", image.FileName, GetFileSizeAsString((ulong)image.OriginalSize));
                DownloadImageAsync(downloadUri, image).Wait();
                Colorizer.WriteLine("[Green!done].");
            }
        }

        private static async Task DownloadImageAsync(string imgDownloadUri, ImageEntity image)
        {
            using (HttpClient client = new HttpClient())
            using (Stream dlStream = await client.GetStreamAsync(imgDownloadUri))
            using (FileStream stream = new FileStream(Path.Combine(s_options.OutputFolder, image.FileName), FileMode.Create,
                FileAccess.Write))
            {
                await dlStream.CopyToAsync(stream);
            }
        }

        private static string[] Units = new[] { "B", "KB", "MB", "GB", "TB" };

        private static string GetFileSizeAsString(ulong sizeInBytes)
        {
            double size = sizeInBytes;
            int idx = 0;
            while (sizeInBytes > 100 && idx < Units.Length - 1)
            {
                size = size / 1024F;
                idx++;

                sizeInBytes = sizeInBytes / 1024;
            }

            return string.Format("{0} {1}", size.ToString("##.##"), Units[idx]);
        }

        private static void ListAlbums()
        {
            // get all the albums from the site.
            var albums = s_user.GetAllAlbumsAsync().Result;

            foreach (var album in albums)
            {
                Colorizer.WriteLine("{0} ([Yellow!{1}])", album.Name, album.EntityId);
            }
        }
    }
}