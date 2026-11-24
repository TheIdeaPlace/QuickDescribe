using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace QuickDescribe.Services;

/// <summary>
/// What a result puts in a saved picture, in the same fields as My Photo Library Described (its FileFields.cs and
/// XmpPacket.cs), so photo apps, File Explorer and that app read it:
///
/// - Caption, XMP's dc:description: the description, then the text read from the picture under "Text in the picture:".
///   What wasn't there (no description, no text) is left out rather than written as "No description: …".
/// - Alt Text (Accessibility), IPTC's AltTextAccessibility: the description, shortened to 250 characters as IPTC asks;
///   with no description, the text read, shortened the same way.
/// - Extended Description (Accessibility), IPTC's ExtDescrAccessibility: the whole caption, when the alt text is less.
/// - Title, dc:title (File Explorer's Title): the alt text of the description; none when there isn't a description,
///   so a screen's text doesn't become a title.
/// </summary>
public sealed partial record EmbeddedDescription(string? Description, string? Text)
{
    /// <summary>IPTC's guidance for Alt Text (Accessibility): 250 characters or fewer.</summary>
    public const int AltTextLimit = 250;

    /// <summary>The fields for a result; null when it has neither a description nor text.</summary>
    public static EmbeddedDescription? For(Result result)
    {
        var description = string.IsNullOrWhiteSpace(result.Description) ? null : XmlSafe(result.Description).Trim();
        if (description?.Length == 0) description = null;
        var lines = result.Lines?.Select(l => XmlSafe(l).Trim()).Where(l => l.Length > 0).ToList();
        var text = lines is { Count: > 0 } ? string.Join(Environment.NewLine, lines) : null;
        return description is null && text is null ? null : new EmbeddedDescription(description, text);
    }

    /// <summary>The text without the characters XML can't hold (control characters, half an emoji), which the text read
    /// or a description could have, and which would make writing the packet throw.</summary>
    internal static string XmlSafe(string text)
    {
        var safe = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i]))
            {
                safe.Append(text, i, 2);
                i++;
            }
            else if (XmlConvert.IsXmlChar(text[i])) safe.Append(text[i]);
        }
        return safe.ToString();
    }

    public string Caption => string.Join(Environment.NewLine + Environment.NewLine,
        new[] { Description, Text is null ? null : Result.TextHeading + Environment.NewLine + Text }.OfType<string>());

    public string AltText => Shorten(Description ?? Text ?? "");

    public string? ExtendedDescription => AltText == Caption ? null : Caption;

    public string? Title => Description is null ? null : AltText;

    /// <summary>The text when it fits in 250 characters, otherwise the whole sentences from its start that fit. When even
    /// the first sentence is longer, it's cut at a word boundary and ends with "…". As My Photo Library Described's
    /// FileFields.AltText.</summary>
    public static string Shorten(string description)
    {
        var text = description.Trim();
        if (text.Length <= AltTextLimit) return text;
        var fitting = "";
        foreach (Match end in SentenceEnd().Matches(text))
        {
            var candidate = text[..(end.Index + end.Length)].Trim();
            if (candidate.Length <= AltTextLimit) fitting = candidate;
            else break;
        }
        if (fitting.Length > 0) return fitting;

        // One long first sentence: as many whole words as fit with the "…", never half a character.
        var limit = AltTextLimit - 1;
        if (char.IsHighSurrogate(text[limit - 1])) limit--;
        var cut = text[..limit];
        if (!char.IsWhiteSpace(text[limit]))
        {
            var space = cut.Length - 1;
            while (space > 0 && !char.IsWhiteSpace(cut[space])) space--;
            if (space > 0) cut = cut[..space];
        }
        // No comma or dash left hanging before the "…".
        cut = cut.TrimEnd(' ', '\t', '\r', '\n', ',', ';', ':', '-', '–', '—');
        return cut + "…";
    }

    // A sentence ends at ., ! or ? (with any closing quotes or brackets) followed by space or the end, or at a full-width
    // 。！？, which isn't followed by a space.
    [GeneratedRegex(@"[.!?]+[""'”’)\]」』]*(?=\s|$)|[。！？]+[」』”’)）]*")]
    private static partial Regex SentenceEnd();

    // ---- The XMP packet ----

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Meta = "adobe:ns:meta/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Iptc = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";

    /// <summary>The fields as an XMP packet, in its wrapper, without padding: the file is written whole, never edited in
    /// place.</summary>
    public byte[] Packet(bool withExtended = true)
    {
        static XElement LangAlt(XName name, string value) =>
            new(name, new XElement(Rdf + "Alt", new XElement(Rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), value)));
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", ""),
            new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(XNamespace.Xmlns + "Iptc4xmpCore", Iptc),
            LangAlt(Dc + "description", Caption));
        if (Title is not null) description.Add(LangAlt(Dc + "title", Title));
        description.Add(LangAlt(Iptc + "AltTextAccessibility", AltText));
        if (withExtended && ExtendedDescription is not null) description.Add(LangAlt(Iptc + "ExtDescrAccessibility", ExtendedDescription));
        var root = new XElement(Meta + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", Meta),
            new XElement(Rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", Rdf), description));
        var buffer = new MemoryStream();
        // Carriage returns in values stay escaped (&#xD;), or a reader would normalise them away.
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(false),
            NewLineHandling = NewLineHandling.Entitize,
            ConformanceLevel = ConformanceLevel.Fragment,
        }))
            root.WriteTo(writer);
        return [.. Encoding.UTF8.GetBytes("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"), .. buffer.ToArray(),
            .. Encoding.UTF8.GetBytes("<?xpacket end=\"w\"?>")];
    }

    /// <summary>The caption, alt text, extended description and title in a packet, null for each it doesn't have.</summary>
    public static (string? Caption, string? AltText, string? ExtendedDescription, string? Title) Fields(ReadOnlySpan<byte> packet)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(packet));
        string? Get(XName name) => document.Descendants(name).FirstOrDefault()?.Descendants(Rdf + "li").FirstOrDefault()?.Value;
        return (Get(Dc + "description"), Get(Iptc + "AltTextAccessibility"), Get(Iptc + "ExtDescrAccessibility"), Get(Dc + "title"));
    }

    // ---- Into the file ----

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private const string PngXmpKeyword = "XML:com.adobe.xmp";
    private static readonly byte[] JpegXmpSignature = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");

    /// <summary>The largest XMP packet one JPEG segment holds: its length counts itself and the signature.</summary>
    internal static int JpegPacketLimit => 0xFFFF - 2 - JpegXmpSignature.Length;

    /// <summary>A PNG or JPEG Quick Describe just encoded, with the fields added: as an iTXt chunk before the picture, or
    /// an APP1 segment after JFIF's. Every other byte is copied as it was. Throws <see cref="InvalidDataException"/> for
    /// anything else, or a file that already has XMP (Quick Describe's encoders write none).</summary>
    public byte[] AddTo(byte[] file)
    {
        if (file.AsSpan().StartsWith(PngSignature)) return AddToPng(file, Packet());
        if (file.Length >= 4 && file[0] == 0xFF && file[1] == 0xD8)
        {
            // Rare: a camera picture with a long text, more than one JPEG segment holds. First without the extended
            // description (it repeats the caption), then without the text read, then with only the alt text's share of it.
            var packet = new Func<byte[]?>[]
                {
                    () => Packet(),
                    () => Packet(withExtended: false),
                    () => Description is null ? null : (this with { Text = null }).Packet(),
                    () => new EmbeddedDescription(Description, Description is null ? AltText : null).Packet(withExtended: false),
                }
                .Select(make => make()).FirstOrDefault(p => p is not null && p.Length <= JpegPacketLimit)
                ?? throw new InvalidDataException("The description is too long for a JPEG.");
            return AddToJpeg(file, packet);
        }
        throw new InvalidDataException("Only PNG and JPEG pictures can hold a description.");
    }

    private static byte[] AddToPng(byte[] png, byte[] packet)
    {
        var at = PngSignature.Length;
        int? before = null;
        while (at + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            if (length < 0 || at + 12 + length > png.Length) break;
            if (type == "iTXt" && png.AsSpan(at + 8, length).StartsWith(Encoding.ASCII.GetBytes(PngXmpKeyword + "\0")))
                throw new InvalidDataException("The picture already has XMP.");
            if (type is "IDAT" or "IEND")
            {
                before ??= at;
                break;
            }
            at += 12 + length;
        }
        if (before is not { } insert) throw new InvalidDataException("The PNG can't be followed.");
        // Keyword, then no compression (flag and method 0), no language and no translated keyword, then the packet.
        byte[] data = [.. Encoding.ASCII.GetBytes(PngXmpKeyword), 0, 0, 0, 0, 0, .. packet];
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes("iTXt").CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return [.. png.AsSpan(0, insert), .. chunk, .. png.AsSpan(insert)];
    }

    private static byte[] AddToJpeg(byte[] jpeg, byte[] packet)
    {
        // After SOI and JFIF's APP0 (and EXIF's APP1, were there one), as XMP's guidance places it.
        var insert = 2;
        var at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            if (marker is 0xDA or 0xD9) break; // the picture, or its end
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
            if (length < 2 || at + 2 + length > jpeg.Length) throw new InvalidDataException("The JPEG can't be followed.");
            if (marker == 0xE1 && jpeg.AsSpan(at + 4, length - 2).StartsWith(JpegXmpSignature))
                throw new InvalidDataException("The picture already has XMP.");
            at += 2 + length;
            if (marker is 0xE0 or 0xE1 && insert == at - 2 - length) insert = at;
        }
        var segment = new byte[4 + JpegXmpSignature.Length + packet.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(segment.Length - 2));
        JpegXmpSignature.CopyTo(segment, 4);
        packet.CopyTo(segment, 4 + JpegXmpSignature.Length);
        return [.. jpeg.AsSpan(0, insert), .. segment, .. jpeg.AsSpan(insert)];
    }

    /// <summary>The packet a PNG or JPEG holds, for reading back; null when it has none.</summary>
    public static byte[]? PacketIn(byte[] file)
    {
        if (file.AsSpan().StartsWith(PngSignature))
        {
            var prefix = Encoding.ASCII.GetBytes(PngXmpKeyword + "\0");
            for (var at = PngSignature.Length; at + 12 <= file.Length;)
            {
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
                if (length < 0 || at + 12 + length > file.Length) return null;
                var data = file.AsSpan(at + 8, length);
                if (Encoding.ASCII.GetString(file, at + 4, 4) == "iTXt" && data.StartsWith(prefix))
                    return data[(prefix.Length + 4)..].ToArray(); // after the flag, method and two empty strings
                at += 12 + length;
            }
            return null;
        }
        for (var at = 2; at + 4 <= file.Length && file[at] == 0xFF && file[at + 1] is not (0xDA or 0xD9);)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at + 2));
            if (length < 2 || at + 2 + length > file.Length) return null;
            var data = file.AsSpan(at + 4, length - 2);
            if (file[at + 1] == 0xE1 && data.StartsWith(JpegXmpSignature)) return data[JpegXmpSignature.Length..].ToArray();
            at += 2 + length;
        }
        return null;
    }

    /// <summary>Adds the fields to a picture already saved, written beside it and swapped in whole, so a failure part way
    /// leaves the picture as it was. The file was written seconds ago, when OneDrive or the search indexer is most likely
    /// to have it open, so a sharing violation is tried again, twice, a moment later. Call it off the UI thread.</summary>
    public void AddToFile(string path, int[]? retryDelaysMs = null)
    {
        var with = AddTo(File.ReadAllBytes(path));
        // A name of its own: Save Described Image could be saving over this same copy (it writes path + ".partial").
        var partial = $"{path}.{Guid.NewGuid():N}{PartialExtension}";
        try
        {
            File.WriteAllBytes(partial, with);
            foreach (var delay in (retryDelaysMs ?? [500, 2000]).Append(-1))
            {
                try
                {
                    File.Move(partial, path, overwrite: true);
                    break;
                }
                // Replacing a file another process has open without delete sharing is "Access is denied", not a sharing
                // violation (checked on Kelly's PC), so both are tried again.
                catch (Exception ex) when (delay >= 0 && ex is UnauthorizedAccessException
                                               or IOException and not (FileNotFoundException or DirectoryNotFoundException))
                {
                    Thread.Sleep(delay);
                }
            }
        }
        catch
        {
            try { File.Delete(partial); }
            catch (Exception) { /* nothing more to do */ }
            throw;
        }
    }

    internal const string PartialExtension = ".partial";

    /// <summary>Removes what a write cut short left in a folder: Quick Describe quit, or Windows signed out, part way
    /// through adding the fields to a copy. Only the folder save everything uses, which is Quick Describe's own.</summary>
    public static void RemoveLeftovers(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var leftover in Directory.EnumerateFiles(folder, "*" + PartialExtension))
            {
                try { File.Delete(leftover); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use: next time */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nothing more to do */ }
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    internal static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
