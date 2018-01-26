using System;
using System.IO;

namespace SharpPackage
{
    class Program
    {
        private const string root = @"C:\Temp\Extracted\";
        private const string packFile = @"C:\temp\1.pak";

        static void Main(string[] args)
        {
            //Create(packFile);
            //Extract(packFile);
            ExtractStream(packFile);
            Console.WriteLine("done");
            Console.Read();
        }

        private static void Extract(string packFile)
        {
            var package = Package.Open(packFile);
            var packItems = package.Read();
            package.Extract(packItems, root, true);
            Console.WriteLine(packItems.Count);
        }

        private static void ExtractStream(string packFile)
        {
            var package = Package.Open(packFile);
            var packItems = package.Read();
            package.Extract(packItems, (item) =>
            {
                var targetFile = Path.Combine(root, item.Name);
                if (File.Exists(targetFile))
                {
                    throw new InvalidOperationException($"File already exists: {targetFile}");
                }
                var path = Path.GetDirectoryName(targetFile);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                using (var fileStream = new FileStream(targetFile, FileMode.CreateNew))
                {
                    item.Stream.Seek(0, SeekOrigin.Begin);
                    package.CopyStream(item.Stream, fileStream, item.Size);
                }
            });
            Console.WriteLine(packItems.Count);
        }

        private static void Create(string packFile)
        {
            var root = @"C:\Temp\wumanber-master";
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            var package = Package.Create(packFile, CompressionMethod.Deflate);
            foreach (var item in files)
            {
                var info = new FileInfo(item);
                using (var fileStream = File.OpenRead(item))
                {
                    package.Add(new Part { Name = item.Substring(root.Length + 1), CreatedDate = info.CreationTime, Size = info.Length, Stream = fileStream });
                }
            }
            package.Save();
        }
    }
}
