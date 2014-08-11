using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmugMugModel;
using System.IO;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Net;

namespace SyncSmugMugPhotos_Parallel
{
    public static class SmugMugExtensions
    {
        public static string GetAlbumPath(this Album album)
        {
            //if the album is inside a subcategory, only include the subcategory in the name.
            //if the album is inside a category..

            string location = string.Empty;
            if (album.SubCategory != null)
            {
                location = "[" + album.SubCategory.Name + "]";
            }
            else
            {
                location = "[" + album.Category.Name + "]";
            }

            location = location + " - " + album.Title;
            return location;
        }
        public static string GetImagePath(this SmugMugModel.Image image)
        {
            string location = image.Album.GetAlbumPath();

            return Path.Combine(location, image.FileName);
        }
    }

    class Program
    {
        static IEnumerable<FileInfo> GetAllDirectories(string dir)
        {
            if (!Directory.Exists(dir))
            {
                yield break;
            }

            DirectoryInfo rootDir = new DirectoryInfo(dir);
            Stack<DirectoryInfo> dirStructure = new Stack<DirectoryInfo>();
            dirStructure.Push(rootDir);

            while (dirStructure.Count > 0)
            {
                DirectoryInfo currDir = dirStructure.Pop();

                // push all the dirs in the structure.
                foreach (var d in currDir.GetDirectories())
                {
                    dirStructure.Push(d);
                }

                // Yield these ones
                foreach (FileInfo file in currDir.GetFiles())
                {
                    yield return file;
                }
            }

            yield break;
        }

        static List<string> GetAllDownloadedFiles(string dir)
        {
            List<string> existingFiles = new List<string>();
            IEnumerable<FileInfo> fi = GetAllDirectories(dir);
            fi.AsParallel().ForAll((file) =>
            {
                Progress();
                System.Drawing.Image img = null;
                try
                {
                    img = Bitmap.FromFile(file.FullName);
                }
                catch
                {
                    Console.WriteLine("Could not load file: {0}", file.FullName);
                }

                if (img != null)
                {
                    //this is a valid image (not corrupted)
                    existingFiles.Add(file.FullName.Replace(dir + "\\", ""));
                }
            });

            return existingFiles;
        }

        static int Main(string[] args)
        {
            string localPath = @"P:\zz_JPEG_PHOTOS_retina";

            string ApiKey = string.Empty;
            string ApiSecret = string.Empty;

            Console.WriteLine("Please provide Api Key:");
            ApiKey = Console.ReadLine();
            Console.WriteLine("Please provide Api secret:");
            ApiSecret = Console.ReadLine();

            args = new string[] { localPath };

            Console.WriteLine("Reading downloaded files...");

            List<string> imagesOnFileSystem = GetAllDownloadedFiles(localPath);
            Console.WriteLine();

            Console.WriteLine("Found {0} files.", imagesOnFileSystem.Count);

            Site smugmug = new Site(ApiKey, ApiSecret);
            Console.WriteLine("Logging in...");
            var accessToken = SmugMugModel.Utility.SmugMugAuthorize.AuthorizeSmugMug(smugmug);

            MyUser user = smugmug.Login(accessToken);
            Console.WriteLine("Done!");

            if (user == null)
            {
                Console.WriteLine("Could not login.");
                return 1;
            }

            // Get the files on SmugMug
            Dictionary<string, string> imagesOnSmugMug = GetFilesOnSmugMug(user);

            // Start downloading images
            StringBuilder errors = new StringBuilder();
            Console.WriteLine("Downloading images");

            Dictionary<string, string> images = new Dictionary<string, string>();

            //determine which files have to be downloaded
            while (imagesOnSmugMug.Count != 0)
            {
                var url = imagesOnSmugMug.Keys.First();
                var imageOnDisk = imagesOnSmugMug[url];
                //do we already have the image in the correct album?
                if (imagesOnFileSystem.Contains(imageOnDisk))
                {
                    // what is left are images that no longer exist on smugmug.
                    imagesOnSmugMug.Remove(url);
                    imagesOnFileSystem.Remove(imageOnDisk);
                    continue;
                }

                images.Add(url, imageOnDisk);
                //we now remove the image from the list and write the temp file.
                imagesOnSmugMug.Remove(url);
            }

            Console.WriteLine();

            toDownload = images.Count;
            // start downloading the images in parallel
            images.AsParallel().ForAll((imageKeys) =>
            {
                var value = Interlocked.Decrement(ref toDownload);

                // use this value to figure out which thread should write to the file.
                lock (images)
                //if (value % Environment.ProcessorCount == 0)
                {
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write("{0} remaining...", value);
                }

                var url = imageKeys.Key;
                var imageOnDisk = images[url];
                string dest = Path.Combine(localPath, imageOnDisk);

                try
                {
                    FileInfo fi = new FileInfo(dest);
                    var destFolder = fi.DirectoryName;
                    Directory.CreateDirectory(destFolder);

                    WebClient wc = new WebClient();
                    wc.DownloadFile(url, dest);
                }
                catch (Exception e)
                {
                    errors.AppendLine(string.Format("{0}:\n{1}\n=========\n", dest, e));
                }

                lock (images)
                //if (value % Environment.ProcessorCount == 0)
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    using (FileStream fs = new FileStream("unfinishedDownload.dat", FileMode.Create, FileAccess.Write))
                    {
                        bf.Serialize(fs, images);
                    }
                }
            });

            if (toDownload == 0)
            {
                File.Delete("unfinishedDownload.dat");
            }

            return 0;
        }

        static int toDownload = 0;


        static Dictionary<string, string> GetFilesOnSmugMug(MyUser user)
        {
            Dictionary<string, string> images = new Dictionary<string, string>();
            BinaryFormatter bf = new BinaryFormatter();

            //do we have unfinished downloads?
            if (File.Exists("unfinishedDownload.dat"))
            {
                using (FileStream fs = new FileStream("unfinishedDownload.dat", FileMode.Open, FileAccess.Read))
                {
                    images = bf.Deserialize(fs) as Dictionary<string, string>;
                }
            }
            else
            {
                Console.WriteLine("Retrieving albums...");
                var albums = user.GetAlbums(true);
                Console.WriteLine("Done!");

                foreach (var album in albums)
                {
                    Console.Write("Retrieving images for {0} ... ", album.Title);
                    foreach (var image in album.GetImages(false, "XLargeURL,FileName", false, 0, false, ""))
                    {
                        images.Add(image.XLargeURL, image.GetImagePath());
                    }
                }

                // save the list
                using (FileStream fs = new FileStream("unfinishedDownload.dat", FileMode.Create, FileAccess.Write))
                {
                    bf.Serialize(fs, images);
                }
            }

            return images;
        }


        static int pos = 0;
        static char[] disp = new char[] { '-', '\\', '|', '/' };
        static void Progress()
        {
            Interlocked.Increment(ref pos);

            Console.Write("\b{0}", disp[pos++ % 4]);
        }

    }
}
