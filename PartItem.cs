namespace SharpPackage
{
    public class PartItem : BasePart
    {
        public long StartPosition { get; internal set; }
        public long EndPosition { get; internal set; }
        public long CompressedSize { get; internal set; }
    }
}
