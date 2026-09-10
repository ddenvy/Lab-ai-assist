using System.Text;

namespace LabAi.Infrastructure.Parsing;

/// <summary>Decodes UTF-8 document bytes, transparently removing a UTF-8 byte order mark.</summary>
internal static class TextDecoder
{
    public static string Decode(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);

        return Encoding.UTF8.GetString(content);
    }
}
