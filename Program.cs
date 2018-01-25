using System;
using System.IO;

namespace SharpPackage
{
    class Program
    {
        static void Main(string[] args)
        {
            var packFile = @"C:\temp\1.pak";
            Create(packFile);
            Extract(packFile);
            Console.Read();
        }

        private static void Extract(string packFile)
        {
            var root = @"C:\Temp\Extracted\";
            var package = Package.Open(packFile);
            var packItems = package.Read();
            package.Extract(packItems, root, true);
            Console.WriteLine(packItems.Count);
        }

        private static void Create(string packFile)
        {
            var root = @"C:\Temp\FirstResponderKit";
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
