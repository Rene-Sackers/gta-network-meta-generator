namespace MetaGenerator
{
    public class FileExtensionMapping
    {
        public string Extension { get; }

        public FileTypes FileType { get; }

        public FileExtensionMapping(string extension, FileTypes fileType)
        {
            Extension = extension;
            FileType = fileType;
        }
    }
}
