using System.IO.Compression;
using System.Text;

namespace StudyRoom.Services;

public class MaterialStorageService
{
    private readonly string _storageRoot;

    public MaterialStorageService(IWebHostEnvironment env, IConfiguration config)
    {
        var relPath = config["MaterialsStorage:DirectoryPath"] ?? "App_Data/Materials";
        _storageRoot = Path.Combine(env.ContentRootPath, relPath);
        Directory.CreateDirectory(_storageRoot);
    }

    public string StorageRoot => _storageRoot;

    public async Task<(string storedFileName, string originalFileName)> SaveMaterialAsync(IFormFile file, string? existingStoredFileName = null)
    {
        if (!string.IsNullOrWhiteSpace(existingStoredFileName))
        {
            DeleteMaterial(existingStoredFileName);
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".pdf", ".docx", ".doc", ".pptx", ".txt", ".md", ".html" };
        if (!allowed.Contains(ext))
        {
            throw new InvalidOperationException($"Недопустимый формат файла ({ext}). Разрешены: PDF, DOCX, DOC, PPTX, TXT, MD, HTML.");
        }

        var safeStoredName = $"lecture_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_storageRoot, safeStoredName);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(stream);

        return (safeStoredName, Path.GetFileName(file.FileName));
    }

    public void DeleteMaterial(string? storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName)) return;
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_storageRoot, safeName);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public string? GetFullFilePath(string? storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName)) return null;
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_storageRoot, safeName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    public string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain; charset=utf-8",
            ".md" => "text/markdown; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Генерирует демонстрационный PDF-файл в защищённом каталоге материалов при первичном заполнении БД.
    /// </summary>
    public string EnsureSeedPdfFile(string storedFileName, string titleLatin, string subtitleLatin)
    {
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_storageRoot, safeName);
        if (File.Exists(fullPath))
            return safeName;

        var contentStream = $"BT /F1 18 Tf 50 760 Td ({EscapePdfText(titleLatin)}) Tj 0 -30 Td /F1 12 Tf ({EscapePdfText(subtitleLatin)}) Tj 0 -25 Td (Electronic Study Room - Protected Lecture Material) Tj ET";
        var streamBytes = Encoding.ASCII.GetBytes(contentStream);

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        var offset1 = sb.Length;
        sb.AppendLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");
        var offset2 = sb.Length;
        sb.AppendLine("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj");
        var offset3 = sb.Length;
        sb.AppendLine("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj");
        var offset4 = sb.Length;
        sb.AppendLine($"4 0 obj << /Length {streamBytes.Length} >> stream");
        sb.AppendLine(contentStream);
        sb.AppendLine("endstream endobj");
        var offset5 = sb.Length;
        sb.AppendLine("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");
        var xrefOffset = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine("0 6");
        sb.AppendLine("0000000000 65535 f ");
        sb.AppendLine($"{offset1:D10} 00000 n ");
        sb.AppendLine($"{offset2:D10} 00000 n ");
        sb.AppendLine($"{offset3:D10} 00000 n ");
        sb.AppendLine($"{offset4:D10} 00000 n ");
        sb.AppendLine($"{offset5:D10} 00000 n ");
        sb.AppendLine("trailer << /Size 6 /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xrefOffset.ToString());
        sb.AppendLine("%%EOF");

        File.WriteAllText(fullPath, sb.ToString(), Encoding.ASCII);
        return safeName;
    }

    /// <summary>
    /// Генерирует демонстрационный DOCX-файл (OpenXML) в защищённом каталоге материалов.
    /// </summary>
    public string EnsureSeedDocxFile(string storedFileName, string title, IEnumerable<string> paragraphs)
    {
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_storageRoot, safeName);
        if (File.Exists(fullPath))
            return safeName;

        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(contentTypesEntry.Open(), Encoding.UTF8))
        {
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
        }

        var relsEntry = archive.CreateEntry("_rels/.rels");
        using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
        {
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
        }

        var docEntry = archive.CreateEntry("word/document.xml");
        using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
        {
            var bodyBuilder = new StringBuilder();
            bodyBuilder.Append($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(title)}</w:t></w:r></w:p>");
            foreach (var p in paragraphs)
            {
                bodyBuilder.Append($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(p)}</w:t></w:r></w:p>");
            }

            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {bodyBuilder}
                  </w:body>
                </w:document>
                """);
        }

        return safeName;
    }

    private static string EscapePdfText(string input)
        => input.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

