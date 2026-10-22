using System.IO.Compression;
using System.Text;
using MyMemory.Server.Models;

namespace MyMemory.Server.Services;

/// <summary>
/// DOCX 导出（承载模块 B04 文档导出）。
///
/// 实现要点：直接用 OOXML 拼装最小可用文档包，不依赖 Office 组件、不调用外部转换服务
/// （技术方案 8.3 决策四）。生成的包只包含 3 个必需部件：
/// [Content_Types].xml、_rels/.rels、word/document.xml。
/// 正文按段落切分，保留用户原有的换行结构，不做任何二次改写。
/// </summary>
public sealed class DocxExportService
{
    /// <summary>导出正文字符上限，超出后提示精简（对应 MYM_4002）。</summary>
    public const int MaxBodyLength = 50_000;

    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
        """;

    private const string RootRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
        """;

    public ExportOutcome Export(ExportRequest? request)
    {
        // 1. 标题与正文都必须有值，否则导出的文档没有意义
        var title = (request?.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return ExportOutcome.Fail(ErrorCodes.ExportTitleEmpty);
        }

        var body = (request?.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return ExportOutcome.Fail(ErrorCodes.ExportBodyEmpty);
        }

        // 2. 格式只接受 docx（当前唯一的导出格式）
        var format = (request?.Format ?? "docx").Trim().ToLowerInvariant();
        if (format is not ("docx" or ""))
        {
            return ExportOutcome.Fail(ErrorCodes.InvalidRequest);
        }

        // 3. 内容长度上限
        if (body.Length > MaxBodyLength)
        {
            return ExportOutcome.Fail(ErrorCodes.ExportTooLarge);
        }

        try
        {
            var documentXml = BuildDocumentXml(title, body);
            var content = BuildPackage(documentXml);
            return ExportOutcome.Ok(content, BuildFileName(title));
        }
        catch (Exception)
        {
            // 拼装失败不抛出到框架层，统一为可重试的 4xxx 错误，用户内容不受影响
            return ExportOutcome.Fail(ErrorCodes.ExportFailed);
        }
    }

    /// <summary>文件名规则：记录标题 + .docx，剔除文件系统不允许的字符。</summary>
    internal static string BuildFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(title.Length);
        foreach (var ch in title)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var safe = builder.ToString().Trim();
        if (safe.Length == 0)
        {
            safe = "传记";
        }

        return $"{safe}.docx";
    }

    /// <summary>
    /// 组装 document.xml：居中大标题 + 正文段落。
    /// 正文以换行为界拆段，空行保留为段落间距，保证用户排版意图不丢。
    /// </summary>
    private static string BuildDocumentXml(string title, string body)
    {
        var builder = new StringBuilder(4096);
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append(
            """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>""");

        // 标题段落：22pt 加粗居中，段后 12pt
        builder.Append("<w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"240\"/></w:pPr><w:r><w:rPr>");
        builder.Append(RunFonts("黑体", "44", bold: true));
        builder.Append("</w:rPr><w:t xml:space=\"preserve\">");
        builder.Append(EscapeXml(title));
        builder.Append("</w:t></w:r></w:p>");

        foreach (var line in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                // 空行：仅保留一个空段落，避免连续空行撑开版面
                builder.Append("<w:p/>");
                continue;
            }

            builder.Append("<w:p><w:pPr><w:ind w:firstLine=\"480\"/>");
            builder.Append("<w:spacing w:line=\"360\" w:lineRule=\"auto\" w:after=\"120\"/></w:pPr><w:r><w:rPr>");
            builder.Append(RunFonts("宋体", "24", bold: false));
            builder.Append("</w:rPr><w:t xml:space=\"preserve\">");
            builder.Append(EscapeXml(trimmed));
            builder.Append("</w:t></w:r></w:p>");
        }

        // 页面设置：A4 纵向，上下 2.54cm、左右 3.17cm
        builder.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>");
        builder.Append("<w:pgMar w:top=\"1440\" w:right=\"1800\" w:bottom=\"1440\" w:left=\"1800\" ");
        builder.Append("w:header=\"851\" w:footer=\"992\" w:gutter=\"0\"/></w:sectPr>");
        builder.Append("</w:body></w:document>");
        return builder.ToString();
    }

    private static string RunFonts(string eastAsia, string halfPointSize, bool bold) =>
        $"<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\" w:eastAsia=\"{eastAsia}\"/>"
        + (bold ? "<w:b/>" : string.Empty)
        + $"<w:sz w:val=\"{halfPointSize}\"/><w:szCs w:val=\"{halfPointSize}\"/>";

    /// <summary>把 document.xml 打包成最小 DOCX（ZIP）。</summary>
    private static byte[] BuildPackage(string documentXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(archive, "_rels/.rels", RootRelsXml);
            WriteEntry(archive, "word/document.xml", documentXml);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entryStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>XML 转义。用户正文可能包含 &amp;、&lt; 等字符，不转义会导致文档打不开。</summary>
    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;");
}
