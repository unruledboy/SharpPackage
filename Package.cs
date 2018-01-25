using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SharpPackage
{
    public class Package
    {
        private static byte[] Header = new byte[] { 0x10, 0x01, 0x10, 0x01 };
        private const long ToStartPosition = 4;
        private const long LargeBufferSize = 8;
        private const long SmallBufferSize = 4;
        private const long TocEndPosition = ToStartPosition + LargeBufferSize;
        private const long ContentPosition = 100;
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        private readonly Stream _stream;
        private readonly bool _isCreate;
        private CompressionMethod _method;
        private readonly bool _isFile;
        private IList<PartItem> _items = new List<PartItem>();
        private long _lastPartEndPosition;
        private bool _canWrite = true;

        public Package(Stream stream, bool isCreate, CompressionMethod method) : this(stream, isCreate, method, false)
        {
        }

        private Package(Stream stream, bool isCreate, CompressionMethod method, bool isFile)
        {
            _stream = stream;
            _isCreate = isCreate;
            _method = method;
            _isFile = isFile;
            if (_isCreate)
            {
                if (method == CompressionMethod.None)
                    method = CompressionMethod.Deflate;
                _stream.Write(Header, 0, Header.Length);
                SeekContent();
            }
            else
            {
                var header = new byte[Header.Length];
                if (_stream.Read(header, 0, header.Length) != header.Length || !header.SequenceEqual(Header))
                    throw new InvalidOperationException("Invalid package file");
            }
        }

        private void SeekContent()
        {
            _stream.Seek(ContentPosition, SeekOrigin.Begin);
        }

        public static Package Create(string file, CompressionMethod method)
        {
            var stream = new FileStream(file, FileMode.CreateNew);
            return Create(stream, method, true);
        }

        private static Package Create(Stream stream, CompressionMethod method, bool isFile)
        {
            return new Package(stream, true, method, isFile);
        }

        public static Package Create(Stream stream, CompressionMethod method)
        {
            return Create(stream, method, false);
        }

        public static Package Open(string file)
        {
            var stream = new FileStream(file, FileMode.Open);
            return new Package(stream, false, CompressionMethod.None);
        }

        public void Add(Part part)
        {
            if (!_canWrite)
                throw new InvalidOperationException("Package already closed");

            var startPosition = _stream.Position;
            var outputStream = _method == CompressionMethod.Deflate ? (Stream)new DeflateStream(_stream, CompressionMode.Compress, true) : new GZipStream(_stream, CompressionMode.Compress, true);
            using (var deflate = outputStream)
            {
                part.Stream.CopyTo(deflate);
            }
            var endPosition = _stream.Position;
            _lastPartEndPosition = endPosition;

            var length = endPosition - startPosition;
            _items.Add(new PartItem { CreatedDate = part.CreatedDate, Name = part.Name, Size = part.Size, CompressedSize = length, StartPosition = startPosition, EndPosition = endPosition });
        }

        public void Extract(IList<PartItem> items, string directory, bool overwrite)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var targetFile = Path.Combine(directory, item.Name);
                if (File.Exists(targetFile))
                {
                    if (overwrite)
                        File.Delete(targetFile);
                    else
                        throw new InvalidOperationException($"File already exists: {targetFile}");
                }
                _stream.Seek(item.StartPosition, SeekOrigin.Begin);
                var path = Path.GetDirectoryName(targetFile);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                using (var fileStream = new FileStream(targetFile, FileMode.CreateNew))
                {
                    var outputStream = _method == CompressionMethod.Deflate ? (Stream)new DeflateStream(_stream, CompressionMode.Decompress, true) : new GZipStream(_stream, CompressionMode.Decompress, true);
                    using (var deflate = outputStream)
                    {
                        CopyStream(deflate, fileStream, item.Size);
                    }
                }
            }
        }

        private static void CopyStream(Stream input, Stream output, long bytes)
        {
            var buffer = new byte[32768];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }

        public IList<PartItem> Read()
        {
            _stream.Seek(ToStartPosition, SeekOrigin.Begin);
            var buffer = new byte[LargeBufferSize];
            _stream.Read(buffer, 0, buffer.Length);
            var tocStartPosition = BitConverter.ToInt64(buffer, 0);

            _stream.Seek(TocEndPosition, SeekOrigin.Begin);
            buffer = new byte[LargeBufferSize];
            _stream.Read(buffer, 0, buffer.Length);
            var tocEndPosition = BitConverter.ToInt64(buffer, 0);

            buffer = new byte[SmallBufferSize];
            _stream.Read(buffer, 0, buffer.Length);
            _method = (CompressionMethod)BitConverter.ToInt32(buffer, 0);

            buffer = new byte[SmallBufferSize];
            _stream.Read(buffer, 0, buffer.Length);
            var itemCount = BitConverter.ToInt32(buffer, 0);

            _stream.Seek(tocStartPosition, SeekOrigin.Begin);
            var items = new List<PartItem>();
            for (int i = 0; i < itemCount; i++)
            {
                var item = new PartItem();

                buffer = new byte[SmallBufferSize];
                _stream.Read(buffer, 0, buffer.Length);
                var nameSize = BitConverter.ToInt32(buffer, 0);

                buffer = new byte[nameSize];
                _stream.Read(buffer, 0, buffer.Length);
                item.Name = Encoding.UTF8.GetString(buffer);

                buffer = new byte[LargeBufferSize];
                _stream.Read(buffer, 0, buffer.Length);
                item.Size = BitConverter.ToInt64(buffer, 0);

                buffer = new byte[LargeBufferSize];
                _stream.Read(buffer, 0, buffer.Length);
                item.CompressedSize = BitConverter.ToInt64(buffer, 0);

                buffer = new byte[LargeBufferSize];
                _stream.Read(buffer, 0, buffer.Length);
                item.StartPosition = BitConverter.ToInt64(buffer, 0);

                buffer = new byte[LargeBufferSize];
                _stream.Read(buffer, 0, buffer.Length);
                item.EndPosition = BitConverter.ToInt64(buffer, 0);

                buffer = new byte[DateFormat.Length];
                _stream.Read(buffer, 0, buffer.Length);
                item.CreatedDate = DateTime.Parse(Encoding.ASCII.GetString(buffer));

                items.Add(item);
            }

            return items;
        }

        public void Save()
        {
            _stream.Seek(ToStartPosition, SeekOrigin.Begin);
            var tocPosition = _lastPartEndPosition;
            var buffer = BitConverter.GetBytes(tocPosition);
            _stream.Write(buffer, 0, buffer.Length);

            _stream.Seek(tocPosition, SeekOrigin.Begin);

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                buffer = Encoding.UTF8.GetBytes(item.Name);
                var nameSize = BitConverter.GetBytes(buffer.Length);
                _stream.Write(nameSize, 0, nameSize.Length);
                _stream.Write(buffer, 0, buffer.Length);

                buffer = BitConverter.GetBytes(item.Size);
                _stream.Write(buffer, 0, buffer.Length);

                buffer = BitConverter.GetBytes(item.CompressedSize);
                _stream.Write(buffer, 0, buffer.Length);

                buffer = BitConverter.GetBytes(item.StartPosition);
                _stream.Write(buffer, 0, buffer.Length);

                buffer = BitConverter.GetBytes(item.EndPosition);
                _stream.Write(buffer, 0, buffer.Length);

                buffer = Encoding.ASCII.GetBytes(item.CreatedDate.ToString(DateFormat));
                _stream.Write(buffer, 0, buffer.Length);
            }

            buffer = BitConverter.GetBytes(_stream.Position);
            _stream.Seek(TocEndPosition, SeekOrigin.Begin);
            _stream.Write(buffer, 0, buffer.Length);

            buffer = BitConverter.GetBytes((int)_method);
            _stream.Write(buffer, 0, buffer.Length);

            buffer = BitConverter.GetBytes(_items.Count);
            _stream.Write(buffer, 0, buffer.Length);

            _canWrite = false;

            if (_isFile)
                _stream.Close();
        }
    }
}
