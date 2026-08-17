using System.IO;

namespace Markit.Core;

public static class DocumentFileService
{
    public const string OpenFilter = "Markdown (*.md;*.markdown;*.mdown)|*.md;*.markdown;*.mdown|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

    public const string SaveFilter = "Markdown (*.md)|*.md|Markdown (*.markdown)|*.markdown|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

    public const string PdfFilter = "PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*";

    public static string Read(string fileName)
    {
        return File.ReadAllText(fileName);
    }

    public static void Write(string fileName, string markdown)
    {
        File.WriteAllText(fileName, markdown);
    }

    public static bool Exists(string fileName)
    {
        return File.Exists(fileName);
    }

    public static bool IsSupportedDocument(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".mdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FirstSupportedDocument(IEnumerable<string> files)
    {
        return files.FirstOrDefault(IsSupportedDocument);
    }

    public static string DescribeFailure(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "No encontre el archivo indicado.",
            DirectoryNotFoundException => "No encontre la carpeta del archivo.",
            UnauthorizedAccessException => "No se pudo acceder a ese archivo con los permisos actuales.",
            IOException => "No se pudo completar la operacion de archivo.",
            _ => "Ocurrio un error inesperado al trabajar con el archivo."
        };
    }
}
